<#
.SYNOPSIS
    Запускает Tasks.Consumer (подписчик RabbitMQ) с переменными окружения из корневого .env.

.DESCRIPTION
    Скрипт читает <корень_репозитория>/.env, экспортирует переменные в процесс
    и запускает Worker-сервис Tasks.Consumer, который слушает очередь task.completed
    в RabbitMQ и идемпотентно пишет события в таблицу tasks_from_rmq (та же БД, что у API).

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
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_Consumer.ps1"

.EXAMPLE
    # Неинтерактивный запуск без пересборки
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_Consumer.ps1" -NoBuild -NoPause
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
$consumerProject = Join-Path $scriptDir 'Consumer/Tasks.Consumer/Tasks.Consumer.csproj'

if (-not (Test-Path $consumerProject)) {
    throw "Не найден проект consumer: $consumerProject"
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

    Write-Host "[run_Consumer] Читаю .env: $Path" -ForegroundColor Cyan
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
# который читает Tasks.Consumer при настройке Serilog (файловый sink).
if ($envVars.ContainsKey('LOGS_DIR') -and $envVars['LOGS_DIR']) {
    [Environment]::SetEnvironmentVariable('Logs__Directory', $envVars['LOGS_DIR'], 'Process')
    Write-Host ("  {0} = {1}" -f 'Logs__Directory', $envVars['LOGS_DIR']) -ForegroundColor DarkGray
}

# --- Параметры брокера (для подсказок) ---
$mqHost = if ($envVars.ContainsKey('RABBITMQ__HOST')) { $envVars['RABBITMQ__HOST'] } else { 'localhost' }
$mqPort = if ($envVars.ContainsKey('RABBITMQ__PORT')) { $envVars['RABBITMQ__PORT'] } else { '5673' }
$mqMgmtPort = if ($envVars.ContainsKey('RABBITMQ__MANAGEMENT_PORT')) { $envVars['RABBITMQ__MANAGEMENT_PORT'] } else { '15673' }
$exchange = if ($envVars.ContainsKey('RABBITMQ__EXCHANGE')) { $envVars['RABBITMQ__EXCHANGE'] } else { 'task.events' }
$routingKey = if ($envVars.ContainsKey('RABBITMQ__ROUTINGKEY')) { $envVars['RABBITMQ__ROUTINGKEY'] } else { 'task.completed' }
$queue = if ($envVars.ContainsKey('RABBITMQ__QUEUE')) { $envVars['RABBITMQ__QUEUE'] } else { 'tasks.task.completed' }

Write-Host "[run_Consumer] RabbitMQ: ${mqHost}:${mqPort}, exchange=${exchange}, routingKey=${routingKey}" -ForegroundColor Cyan
Write-Host "[run_Consumer] Очередь: $queue" -ForegroundColor Yellow
Write-Host "[run_Consumer] Management UI: http://localhost:${mqMgmtPort} (#/queues)" -ForegroundColor DarkGray

# --- Каталог логов ---
if ($envVars.ContainsKey('LOGS_DIR') -and $envVars['LOGS_DIR']) {
    if (-not (Test-Path $envVars['LOGS_DIR'])) {
        New-Item -ItemType Directory -Path $envVars['LOGS_DIR'] -Force | Out-Null
    }
}

try {
    # --- Проверка брокера (опционально) ---
    if ($CheckBroker) {
        Write-Host "[run_Consumer] Проверяю доступность RabbitMQ ${mqHost}:${mqPort}..." -ForegroundColor Cyan

        $tcp = Test-NetConnection -ComputerName $mqHost -Port ([int]$mqPort) -WarningAction SilentlyContinue
        if (-not $tcp.TcpTestSucceeded) {
            Write-Warning "RabbitMQ на ${mqHost}:${mqPort} недоступен. Подними инфраструктуру: wsl docker compose -f db/docker-compose.yml up -d"
        } else {
            Write-Host "[run_Consumer] RabbitMQ доступен." -ForegroundColor Green
        }
    }

    # --- Сборка ---
    if (-not $NoBuild) {
        Write-Host "[run_Consumer] Сборка $consumerProject ..." -ForegroundColor Cyan
        & dotnet build $consumerProject
        if ($LASTEXITCODE -ne 0) {
            throw "Сборка завершилась с кодом $LASTEXITCODE"
        }
    }

    # --- Запуск ---
    Write-Host "[run_Consumer] Запускаю Tasks.Consumer. Ctrl+C для остановки." -ForegroundColor Green
    $dotnetArgs = @('run', '--project', $consumerProject, '--no-build')
    & dotnet @dotnetArgs
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        Write-Warning "Tasks.Consumer завершился с кодом $exit"
    }
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter для выхода"
    }
}
