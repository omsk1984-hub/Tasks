<#
.SYNOPSIS
    Запускает Tasks.Api с переменными окружения из корневого файла .env.

.DESCRIPTION
    Скрипт читает <корень_репозитория>/.env, экспортирует переменные в процесс
    и запускает ASP.NET Core приложение Tasks.Api (Minimal API).

    Поддерживаются два режима запуска .env:
      - простой формат KEY=VALUE (переменные секций .NET: DB__PORT, RabbitMq__Host и т.п.);
      - значения с пробелами/кавычками берутся в кавычки — кавычки снимаются.

    По умолчанию перед запуском выполняется сборка проекта (-NoBuild отключает).

.PARAMETER Url
    URL, на котором слушает приложение. По умолчанию берётся из .env (ASPNETCORE_URLS)
    либо http://localhost:5080.

.PARAMETER EnvFile
    Путь к .env. По умолчанию — .env в корне репозитория.

.PARAMETER NoBuild
    Не собирать проект перед запуском (использовать уже собранные бинарники).

.PARAMETER NoPause
    Не ждать нажатия клавиши по завершении (для неинтерактивного запуска).

.PARAMETER CheckDb
    Перед стартом проверить доступность PostgreSQL (контейнер Tasks_postgres).

.EXAMPLE
    # Обычный запуск (интерактивно, с паузой в конце)
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/Tasks/run_Tasks.ps1"

.EXAMPLE
    # Неинтерактивный запуск без пересборки
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/Tasks/run_Tasks.ps1" -NoBuild -NoPause
#>
[CmdletBinding()]
param(
    [string]$Url,
    [string]$EnvFile,
    [switch]$NoBuild,
    [switch]$NoPause,
    [switch]$CheckDb
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# UTF-8 для корректной кириллицы в выводе (см. правило проекта по кодировкам).
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
try { chcp 65001 | Out-Null } catch { }

# --- Пути ---
$scriptDir = $PSScriptRoot                              # .../src/Tasks
$repoRoot  = Split-Path -Parent (Split-Path -Parent $scriptDir)  # .../   (два уровня вверх: Tasks -> src -> repo)
$apiProject = Join-Path $scriptDir 'Tasks.Api/Tasks.Api.csproj'

if (-not (Test-Path $apiProject)) {
    throw "Не найден проект API: $apiProject"
}

if (-not $EnvFile) {
    $EnvFile = Join-Path $repoRoot '.env'
}

function Import-DotEnv {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Warning "Файл .env не найден: $Path. Будут использованы значения appsettings.json."
        return @{}
    }

    Write-Host "[run_Tasks] Читаю .env: $Path" -ForegroundColor Cyan
    $vars = @{}
    $lineNo = 0

    foreach ($rawLine in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $lineNo++
        $line = $rawLine.Trim()

        # Пропускаем пустые строки и комментарии.
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) {
            continue
        }

        $eq = $line.IndexOf('=')
        if ($eq -lt 1) {
            Write-Warning "Строка $lineNo в .env не похожа на KEY=VALUE, пропущена: $line"
            continue
        }

        $key = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim()

        # Снимаем обрамляющие кавычки, если есть.
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        if ([string]::IsNullOrWhiteSpace($key)) {
            Write-Warning "Строка $lineNo в .env с пустым ключом, пропущена."
            continue
        }

        $vars[$key] = $value
    }

    return $vars
}

# --- Загрузка .env ---
$envVars = Import-DotEnv -Path $EnvFile
foreach ($key in $envVars.Keys) {
    [Environment]::SetEnvironmentVariable($key, $envVars[$key], 'Process')
    Write-Host ("  {0} = {1}" -f $key, $envVars[$key]) -ForegroundColor DarkGray
}

# --- URL ---
if (-not $Url) {
    if ($envVars.ContainsKey('ASPNETCORE_URLS') -and $envVars['ASPNETCORE_URLS']) {
        $Url = $envVars['ASPNETCORE_URLS']
    } else {
        $Url = 'http://localhost:5080'
    }
}

Write-Host "[run_Tasks] URL: $Url" -ForegroundColor Cyan

# --- Каталог логов ---
if ($envVars.ContainsKey('LOGS_DIR') -and $envVars['LOGS_DIR']) {
    if (-not (Test-Path $envVars['LOGS_DIR'])) {
        New-Item -ItemType Directory -Path $envVars['LOGS_DIR'] -Force | Out-Null
    }
}

try {
    # --- Проверка БД (опционально) ---
    if ($CheckDb) {
        $host_ = if ($envVars.ContainsKey('DB__HOST')) { $envVars['DB__HOST'] } else { 'localhost' }
        $port_ = if ($envVars.ContainsKey('DB__PORT')) { $envVars['DB__PORT'] } else { '5442' }
        Write-Host "[run_Tasks] Проверяю доступность PostgreSQL ${host_}:${port_}..." -ForegroundColor Cyan

        $tcp = Test-NetConnection -ComputerName $host_ -Port ([int]$port_) -WarningAction SilentlyContinue
        if (-not $tcp.TcpTestSucceeded) {
            Write-Warning "PostgreSQL на ${host_}:${port_} недоступен. Подними инфраструктуру: wsl docker compose -f db/docker-compose.yml up -d"
        } else {
            Write-Host "[run_Tasks] PostgreSQL доступен." -ForegroundColor Green
        }
    }

    # --- Сборка ---
    if (-not $NoBuild) {
        Write-Host "[run_Tasks] Сборка $apiProject ..." -ForegroundColor Cyan
        & dotnet build $apiProject
        if ($LASTEXITCODE -ne 0) {
            throw "Сборка завершилась с кодом $LASTEXITCODE"
        }
    }

    # --- Запуск ---
    Write-Host "[run_Tasks] Запускаю Tasks.Api ($Url). Ctrl+C для остановки." -ForegroundColor Green
    $dotnetArgs = @('run', '--project', $apiProject, '--no-build', '--urls', $Url)
    & dotnet @dotnetArgs
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        Write-Warning "Tasks.Api завершился с кодом $exit"
    }
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter для выхода"
    }
}
