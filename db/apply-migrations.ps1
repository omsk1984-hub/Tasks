<#
.SYNOPSIS
    Применяет EF Core миграции к базе Tasks (PostgreSQL).

.DESCRIPTION
    Обёртка над `dotnet ef database update`. Подключение берётся из
    appsettings.json / переменных окружения (ConnectionStrings__Postgres),
    но её можно переопределить параметром -ConnectionString.

    Запуск (в терминале Roo всегда через pwsh -File, см. правило проекта):
      pwsh -NoProfile -ExecutionPolicy Bypass -File "db/apply-migrations.ps1" -NoPause

.PARAMETER ConnectionString
    Явная строка подключения. Если не задана — используется из конфигурации приложения.

.PARAMETER NoPause
    Не ждать нажатия клавиши (для неинтерактивного запуска).

.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File "db/apply-migrations.ps1" -NoPause
#>
[CmdletBinding()]
param(
    [string]$ConnectionString,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# UTF-8 для корректной кириллицы в выводе самого скрипта.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/Tasks/Tasks.Api/Tasks.Api.csproj'

if (-not (Test-Path $apiProject)) {
    throw "Не найден проект API: $apiProject"
}

Write-Host "[apply-migrations] Проект: $apiProject" -ForegroundColor Cyan

$envVars = @{}
if ($ConnectionString) {
    $envVars['ConnectionStrings__Postgres'] = $ConnectionString
    Write-Host "[apply-migrations] Использую переданную строку подключения." -ForegroundColor Yellow
}

$previous = @{}
foreach ($key in $envVars.Keys) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $envVars[$key], 'Process')
}

try {
    Write-Host "[apply-migrations] dotnet ef database update..." -ForegroundColor Cyan
    & dotnet ef database update --project $apiProject --startup-project $apiProject
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef завершился с кодом $LASTEXITCODE"
    }
    Write-Host "[apply-migrations] Миграции применены успешно." -ForegroundColor Green
}
finally {
    foreach ($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
    }

    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter для выхода"
    }
}
