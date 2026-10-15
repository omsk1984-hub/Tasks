<#
.SYNOPSIS
    Быстрый smoke-тест Tasks.Api: создаёт задачу и сразу завершает её.

.DESCRIPTION
    Скрипт последовательно выполняет два вызова REST API:
      1) POST /tasks            — создаёт задачу (CreateTask);
      2) PUT  /tasks/{id}/complete — завершает только что созданную задачу (CompleteTask).

    Идея — за один прогон проверить основной happy-path API (создание + завершение)
    и убедиться, что сервис отвечает корректно.

    Базовый URL берётся из параметра -BaseUrl, иначе из <корень_репозитория>/.env
    (переменная ASPNETCORE_URLS), иначе http://localhost:5080.

    Скрипт возвращает ненулевой код выхода (1), если любой из шагов не удался
    (например, API недоступен или вернул неожиданный статус). Это удобно для CI.

.PARAMETER BaseUrl
    Базовый URL API без завершающего слэша. По умолчанию — из .env (ASPNETCORE_URLS)
    либо http://localhost:5080.

.PARAMETER Title
    Заголовок создаваемой задачи. По умолчанию — с меткой времени UTC.

.PARAMETER Priority
    Приоритет задачи: Low, Medium или High. По умолчанию Medium.

.PARAMETER EnvFile
    Путь к .env. По умолчанию — .env в корне репозитория.

.PARAMETER NoPause
    Не ждать нажатия клавиши по завершении (для неинтерактивного/CI-запуска).

.EXAMPLE
    # Обычный прогон (интерактивно, с паузой в конце)
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/fast_test.ps1"

.EXAMPLE
    # Неинтерактивный прогон для CI
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/fast_test.ps1" -NoPause

.EXAMPLE
    # Явные параметры
    pwsh -NoProfile -ExecutionPolicy Bypass -File "src/fast_test.ps1" -BaseUrl "http://localhost:5080" -Priority High
#>
[CmdletBinding()]
param(
    [string]$BaseUrl,
    [string]$Title,
    [ValidateSet('Low', 'Medium', 'High')]
    [string]$Priority = 'Medium',
    [string]$EnvFile,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# UTF-8 для корректной кириллицы в выводе (см. правило проекта по кодировкам).
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
try { chcp 65001 | Out-Null } catch { }

# Блокируем интерактивный опрос учётных данных в HTTP-вызовах (неинтерактивный smoke-тест).
$PSDefaultParameterValues['Invoke-RestMethod:AllowUnencryptedAuthentication'] = $true

# --- Пути ---
$scriptDir = $PSScriptRoot                     # .../src
$repoRoot  = Split-Path -Parent $scriptDir     # .../   (src -> repo)

if (-not $EnvFile) {
    $EnvFile = Join-Path $repoRoot '.env'
}

function Import-DotEnv {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return @{}
    }

    $vars = @{}
    foreach ($rawLine in Get-Content -LiteralPath $Path -Encoding UTF8) {
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

        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $vars[$key] = $value
        }
    }

    return $vars
}

# --- Определение базового URL ---
if (-not $BaseUrl) {
    $envVars = Import-DotEnv -Path $EnvFile
    if ($envVars.ContainsKey('ASPNETCORE_URLS') -and $envVars['ASPNETCORE_URLS']) {
        $BaseUrl = $envVars['ASPNETCORE_URLS']
    } else {
        $BaseUrl = 'http://localhost:5080'
    }
}
$BaseUrl = $BaseUrl.TrimEnd('/')

if (-not $Title) {
    $Title = "fast_test $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}

$createUri   = "$BaseUrl/tasks"
$tasksUri    = "$BaseUrl/tasks"

# API может отдавать JSON в camelCase (ASP.NET Core по умолчанию) — учитываем оба варианта регистра.
function Get-Prop {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)] [string[]] $Names
    )
    foreach ($name in $Names) {
        if ($null -ne $Object.PSObject.Properties[$name]) {
            return $Object.PSObject.Properties[$name].Value
        }
    }
    return $null
}

Write-Host "[fast_test] API: $BaseUrl" -ForegroundColor Cyan
Write-Host "[fast_test] Title: $Title, Priority: $Priority" -ForegroundColor DarkGray

$exitCode = 0
$createdId = $null

try {
    # --- Шаг 1: создать задачу ---
    Write-Host "[fast_test] 1/2 Создаю задачу (POST $createUri)..." -ForegroundColor Cyan
    $createBody = @{ title = $Title; priority = $Priority } | ConvertTo-Json -Compress

    try {
        $created = Invoke-RestMethod -Method Post -Uri $createUri -ContentType 'application/json; charset=utf-8' -Body $createBody
    } catch {
        Write-Host "[fast_test] Не удалось создать задачу: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }

    $createdId = Get-Prop -Object $created -Names @('id', 'Id')
    if (-not $createdId) {
        throw "Ответ API не содержит идентификатор задачи: $($created | ConvertTo-Json -Compress)"
    }

    $createdTitle = Get-Prop -Object $created -Names @('title', 'Title')
    $createdState = Get-Prop -Object $created -Names @('isCompleted', 'IsCompleted')
    Write-Host "[fast_test] Создана задача: id=$createdId, title='$createdTitle', isCompleted=$createdState" -ForegroundColor Green

    # --- Шаг 2: завершить задачу ---
    $completeUri = "$tasksUri/$createdId/complete"
    Write-Host "[fast_test] 2/2 Завершаю задачу (PUT $completeUri)..." -ForegroundColor Cyan

    try {
        $completed = Invoke-RestMethod -Method Put -Uri $completeUri -ContentType 'application/json; charset=utf-8'
    } catch {
        Write-Host "[fast_test] Не удалось завершить задачу: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }

    $completedState = Get-Prop -Object $completed -Names @('isCompleted', 'IsCompleted')
    $completedAt    = Get-Prop -Object $completed -Names @('completedAt', 'CompletedAt')

    if ($completedState -ne $true) {
        throw "Задача не помечена как завершённая: $($completed | ConvertTo-Json -Compress)"
    }

    Write-Host "[fast_test] Задача завершена: id=$createdId, isCompleted=$completedState, completedAt=$completedAt" -ForegroundColor Green
    Write-Host "[fast_test] OK — happy-path (create + complete) пройден." -ForegroundColor Green
}
catch {
    Write-Host "[fast_test] FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter для выхода"
    }
}

exit $exitCode
