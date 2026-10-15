<#
.SYNOPSIS
    Применяет EF Core миграции к БД Tasks и (опционально) готовит инфраструктуру
    для CDC (пересоздаёт PostgreSQL с wal_level=logical, диагностирует слот/публикацию).

.DESCRIPTION
    Скрипт закрывает весь путь подготовки БД для архитектуры outbox + CDC:

      1. (опционально, -RecreatePostgres) Пересоздаёт контейнер Tasks_postgres
         с параметром wal_level=logical из db/docker-compose.yml.
         Логическое воспроизведение WAL (pgoutput) требует wal_level=logical;
         без него слот создать нельзя. Том данных сохраняется.
      2. Применяет EF Core миграции Tasks.Api (в т.ч. AddOutboxMessages → таблица outbox_messages).
      3. Диагностирует: wal_level, право REPLICATION у tasks_user, наличие таблицы
         outbox_messages, публикации и репликационные слоты.

    Подключение берётся из appsettings.json / переменных окружения
    (ConnectionStrings__Postgres), можно переопределить -ConnectionString.
    Параметры контейнера/БД берутся из .env (DB__HOST/PORT/USER/DATABASE) с дефолтами.

    Запуск (в терминале Roo всегда через pwsh -File, см. правило проекта):
      pwsh -NoProfile -ExecutionPolicy Bypass -File "db/migrate.ps1" -NoPause

.PARAMETER ConnectionString
    Явная строка подключения. Если не задана — используется из конфигурации приложения.

.PARAMETER RecreatePostgres
    Перед миграциями пересоздать контейнер Tasks_postgres (docker compose up -d --force-recreate)
    с wal_level=logical. Нужно один раз, если контейнер был поднят без logical.

.PARAMETER NoPause
    Не ждать нажатия клавиши (для неинтерактивного запуска).

.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File "db/migrate.ps1" -NoPause

.EXAMPLE
    # Полная подготовка: пересоздать Postgres с wal_level=logical и применить миграции
    pwsh -NoProfile -ExecutionPolicy Bypass -File "db/migrate.ps1" -RecreatePostgres -NoPause
#>
[CmdletBinding()]
param(
    [string]$ConnectionString,
    [switch]$RecreatePostgres,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# UTF-8 для корректной кириллицы в выводе самого скрипта.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
try { chcp 65001 | Out-Null } catch { }

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/Tasks/Tasks.Api/Tasks.Api.csproj'
$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

if (-not (Test-Path $apiProject)) {
    throw "Не найден проект API: $apiProject"
}

<#
    Конвертирует Windows-путь (D:\work\...\docker-compose.yml) в WSL-путь
    (/mnt/d/work/.../docker-compose.yml). WSL не понимает обратные слеши и букву диска.
#>
function Convert-ToWslPath {
    param([Parameter(Mandatory)] [string]$WindowsPath)

    $full = [System.IO.Path]::GetFullPath($WindowsPath)
    if ($full -match '^([A-Za-z]):\\(.*)$') {
        $drive = $Matches[1].ToLowerInvariant()
        $rest = $Matches[2].Replace('\', '/')
        return "/mnt/$drive/$rest"
    }

    # Уже unix-путь или иной формат — возвращаем как есть.
    return ($full -replace '\\', '/')
}

# --- Значения по умолчанию (совпадают с db/docker-compose.yml и .env.example) ---
$dbHost = 'localhost'
$dbPort = '5442'
$dbName = 'tasks_db'
$dbUser = 'tasks_user'
$dbPassword = 'tasks_password'

# Читаем .env (если есть) для параметров контейнера/подключения.
$envFile = Join-Path $repoRoot '.env'
if (Test-Path $envFile) {
    Write-Host "[migrate] Читаю .env: $envFile" -ForegroundColor Cyan
    foreach ($rawLine in Get-Content -LiteralPath $envFile -Encoding UTF8) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        switch ($key) {
            'DB__HOST'     { $dbHost = $value }
            'DB__PORT'     { $dbPort = $value }
            'DB__DATABASE' { $dbName = $value }
            'DB__USER'     { $dbUser = $value }
            'DB__PASSWORD' { $dbPassword = $value }
        }
    }
}

Write-Host "[migrate] Postgres: ${dbHost}:${dbPort}/${dbName} (user=${dbUser})" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# 1. (Опционально) Пересоздание PostgreSQL с wal_level=logical
# -----------------------------------------------------------------------------
if ($RecreatePostgres) {
    Write-Host "[migrate] Пересоздаю Tasks_postgres с wal_level=logical..." -ForegroundColor Yellow

    $composeWsl = Convert-ToWslPath $composeFile
    Write-Host "[migrate] compose: $composeWsl" -ForegroundColor DarkGray

    wsl docker compose -f $composeWsl up -d --force-recreate Tasks_postgres
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up завершился с кодом $LASTEXITCODE"
    }

    # Ждём healthcheck.
    $health = 'unknown'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $health = (wsl docker inspect --format '{{.State.Health.Status}}' Tasks_postgres 2>$null)
        if ($health -eq 'healthy') { break }
        Start-Sleep -Seconds 2
    }
    Write-Host "[migrate] Контейнер Tasks_postgres пересоздан (health=$health)." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 2. Применение миграций
# -----------------------------------------------------------------------------
Write-Host "[migrate] dotnet ef database update..." -ForegroundColor Cyan
$envVars = @{}
if ($ConnectionString) {
    $envVars['ConnectionStrings__Postgres'] = $ConnectionString
    Write-Host "[migrate] Использую переданную строку подключения." -ForegroundColor Yellow
}

$previous = @{}
foreach ($key in $envVars.Keys) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $envVars[$key], 'Process')
}

try {
    & dotnet ef database update --project $apiProject --startup-project $apiProject
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef завершился с кодом $LASTEXITCODE"
    }
    Write-Host "[migrate] Миграции применены успешно." -ForegroundColor Green
}
finally {
    foreach ($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
    }
}

# -----------------------------------------------------------------------------
# 3. Диагностика CDC-предпосылок
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "[migrate] Диагностика CDC:" -ForegroundColor Cyan

function Invoke-DbScalar {
    param([Parameter(Mandatory)] [string]$Sql)
    $result = wsl docker exec -e "PGPASSWORD=$dbPassword" Tasks_postgres psql -U $dbUser -d $dbName -tAc $Sql 2>$null
    return ($result | Out-String).Trim()
}

try {
    $walLevel = Invoke-DbScalar -Sql "SHOW wal_level;"
    $repl = Invoke-DbScalar -Sql "SELECT rolreplication FROM pg_roles WHERE rolname = '$dbUser';"
    $outboxExists = Invoke-DbScalar -Sql "SELECT to_regclass('public.outbox_messages') IS NOT NULL;"
    $publications = Invoke-DbScalar -Sql "SELECT coalesce(string_agg(pubname, ','), '-') FROM pg_publication;"
    $slots = Invoke-DbScalar -Sql "SELECT coalesce(string_agg(slot_name, ','), '-') FROM pg_replication_slots;"

    Write-Host ("  wal_level            = {0} {1}" -f $walLevel, $(if ($walLevel -eq 'logical') { 'OK' } else { '(!) нужен logical для CDC' }))
    Write-Host ("  rolreplication       = {0} {1}" -f $repl, $(if ($repl -eq 't') { 'OK' } else { '(!) нужен REPLICATION' }))
    Write-Host ("  outbox_messages      = {0}" -f $outboxExists)
    Write-Host ("  publications         = {0}" -f $publications)
    Write-Host ("  replication_slots    = {0}" -f $slots)

    if ($walLevel -ne 'logical') {
        Write-Warning "wal_level=$walLevel — логическая репликация недоступна. Запустите: db/migrate.ps1 -RecreatePostgres"
    }
    if ($outboxExists -ne 't') {
        Write-Warning "Таблица outbox_messages не найдена — миграция AddOutboxMessages не применена."
    }
}
catch {
    Write-Warning "Не удалось выполнить диагностику через docker exec: $($_.Exception.Message)"
}

if (-not $NoPause) {
    Write-Host ""
    Read-Host "Нажмите Enter для выхода"
}
