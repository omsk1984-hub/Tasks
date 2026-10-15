<#
.SYNOPSIS
    Запускает Tasks.CDC (CDC-воркер outbox → RabbitMQ) с переменными окружения из корневого .env.

.DESCRIPTION
    Скрипт читает <корень_репозитория>/.env, экспортирует переменные в процесс
    и запускает Worker-сервис Tasks.CDC, который читает WAL таблицы outbox_messages
    через логическую репликацию PostgreSQL (pgoutput) и публикует события в RabbitMQ
    (exchange task.events / routing key task.completed).

    Требования к инфраструктуре:
      - PostgreSQL с wal_level=logical (см. db/docker-compose.yml);
      - у пользователя подключения (tasks_user) есть право REPLICATION
        (выдаётся в db/init/01-init.sql при инициализации контейнера).

    Поддерживаются два режима запуска .env:
      - простой формат KEY=VALUE (переменные секций .NET: DB__PORT, RabbitMq__Host и т.п.);
      - значения с пробелами/кавычками берутся в кавычки — кавычки снимаются.

    По умолчанию перед запуском выполняется сборка проекта (-NoBuild отключает).

.PARAMETER EnvFile
    Путь к .env. По умолчанию — .env в корне репозитория.

.PARAMETER NoBuild
    Не собирать проект перед запуском (использовать уже собранные бинарники).

.PARAMETER NoPause
    Не ждать нажатия клавиши по завершении (для неинтерактивного запуска).

.PARAMETER CheckBroker
    Перед стартом проверить доступность RabbitMQ (контейнер Tasks_rabbitmq).

.EXAMPLE
    # Обычный запуск (интерактивно, с паузой в конце)
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_CDC.ps1"

.EXAMPLE
    # Неинтерактивный запуск без пересборки
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_CDC.ps1" -NoBuild -NoPause
#>
[CmdletBinding()]
param(
    [string]$EnvFile,
    [switch]$NoBuild,
    [switch]$NoPause,
    [switch]$CheckBroker
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# UTF-8 для корректной кириллицы в выводе (см. правило проекта по кодировкам).
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
try { chcp 65001 | Out-Null } catch { }

# --- Пути ---
$scriptDir = $PSScriptRoot                              # .../src
$repoRoot  = Split-Path -Parent $scriptDir              # .../   (на уровень вверх: src -> repo)
$cdcProject = Join-Path $scriptDir 'Tasks/Tasks.CDC/Tasks.CDC.csproj'

if (-not (Test-Path $cdcProject)) {
    throw "Не найден проект CDC: $cdcProject"
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

    Write-Host "[run_CDC] Читаю .env: $Path" -ForegroundColor Cyan
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

# LOGS_DIR из .env -> ключ конфигурации .NET Logs:Directory (Logs__Directory),
# который читает Tasks.CDC при настройке Serilog (файловый sink).
if ($envVars.ContainsKey('LOGS_DIR') -and $envVars['LOGS_DIR']) {
    [Environment]::SetEnvironmentVariable('Logs__Directory', $envVars['LOGS_DIR'], 'Process')
    Write-Host ("  {0} = {1}" -f 'Logs__Directory', $envVars['LOGS_DIR']) -ForegroundColor DarkGray
}

# --- Параметры репликации и брокера (для подсказок) ---
$dbHost = if ($envVars.ContainsKey('DB__HOST')) { $envVars['DB__HOST'] } else { 'localhost' }
$dbPort = if ($envVars.ContainsKey('DB__PORT')) { $envVars['DB__PORT'] } else { '5442' }
$slot = if ($envVars.ContainsKey('CDC__SLOTNAME')) { $envVars['CDC__SLOTNAME'] } else { 'tasks_outbox_slot' }
$publication = if ($envVars.ContainsKey('CDC__PUBLICATIONNAME')) { $envVars['CDC__PUBLICATIONNAME'] } else { 'tasks_outbox_pub' }
$outboxTable = if ($envVars.ContainsKey('CDC__OUTBOXTABLE')) { $envVars['CDC__OUTBOXTABLE'] } else { 'outbox_messages' }

$mqHost = if ($envVars.ContainsKey('RABBITMQ__HOST')) { $envVars['RABBITMQ__HOST'] } else { 'localhost' }
$mqPort = if ($envVars.ContainsKey('RABBITMQ__PORT')) { $envVars['RABBITMQ__PORT'] } else { '5673' }
$mqMgmtPort = if ($envVars.ContainsKey('RABBITMQ__MANAGEMENT_PORT')) { $envVars['RABBITMQ__MANAGEMENT_PORT'] } else { '15673' }
$exchange = if ($envVars.ContainsKey('RABBITMQ__EXCHANGE')) { $envVars['RABBITMQ__EXCHANGE'] } else { 'task.events' }
$routingKey = if ($envVars.ContainsKey('RABBITMQ__ROUTINGKEY')) { $envVars['RABBITMQ__ROUTINGKEY'] } else { 'task.completed' }

Write-Host "[run_CDC] PostgreSQL: ${dbHost}:${dbPort}, таблица=${outboxTable}" -ForegroundColor Cyan
Write-Host "[run_CDC] Репликация: slot=${slot}, publication=${publication} (pgoutput)" -ForegroundColor Yellow
Write-Host "[run_CDC] RabbitMQ: ${mqHost}:${mqPort}, exchange=${exchange}, routingKey=${routingKey}" -ForegroundColor Cyan
Write-Host "[run_CDC] Management UI: http://localhost:${mqMgmtPort} (#/exchanges)" -ForegroundColor DarkGray
Write-Host "[run_CDC] Требуется wal_level=logical и право REPLICATION у пользователя БД." -ForegroundColor DarkGray

# --- Каталог логов ---
if ($envVars.ContainsKey('LOGS_DIR') -and $envVars['LOGS_DIR']) {
    if (-not (Test-Path $envVars['LOGS_DIR'])) {
        New-Item -ItemType Directory -Path $envVars['LOGS_DIR'] -Force | Out-Null
    }
}

try {
    # --- Проверка брокера (опционально) ---
    if ($CheckBroker) {
        Write-Host "[run_CDC] Проверяю доступность RabbitMQ ${mqHost}:${mqPort}..." -ForegroundColor Cyan

        $tcp = Test-NetConnection -ComputerName $mqHost -Port ([int]$mqPort) -WarningAction SilentlyContinue
        if (-not $tcp.TcpTestSucceeded) {
            Write-Warning "RabbitMQ на ${mqHost}:${mqPort} недоступен. Подними инфраструктуру: wsl docker compose -f db/docker-compose.yml up -d"
        } else {
            Write-Host "[run_CDC] RabbitMQ доступен." -ForegroundColor Green
        }
    }

    # --- Сборка ---
    if (-not $NoBuild) {
        Write-Host "[run_CDC] Сборка $cdcProject ..." -ForegroundColor Cyan
        & dotnet build $cdcProject
        if ($LASTEXITCODE -ne 0) {
            throw "Сборка завершилась с кодом $LASTEXITCODE"
        }
    }

    # --- Запуск ---
    Write-Host "[run_CDC] Запускаю Tasks.CDC. Ctrl+C для остановки." -ForegroundColor Green
    $dotnetArgs = @('run', '--project', $cdcProject, '--no-build')
    & dotnet @dotnetArgs
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        Write-Warning "Tasks.CDC завершился с кодом $exit"
    }
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter для выхода"
    }
}
