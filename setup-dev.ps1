# setup-dev.ps1 — Быстрая настройка окружения разработки Konserva
# Запускать из корня репозитория (рядом с global.json)

$ErrorActionPreference = "Stop"
$script:repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=== Konserva Development Setup ===" -ForegroundColor Cyan
Write-Host ""

# 1. Проверяем .NET SDK
Write-Host "[1/4] Checking .NET SDK..." -ForegroundColor Yellow
try {
    $sdkVersion = dotnet --version
    Write-Host "  .NET SDK $sdkVersion found" -ForegroundColor Green
}
catch {
    Write-Host "  .NET SDK не найден. Установите .NET 10 SDK:" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor White
    exit 1
}

# 2. Проверяем global.json
Write-Host "[2/4] Checking global.json..." -ForegroundColor Yellow
$globalJson = Join-Path $script:repoRoot "global.json"
if (Test-Path $globalJson) {
    Write-Host "  global.json found" -ForegroundColor Green
    dotnet --list-sdks | Select-String "10\."
}
else {
    Write-Host "  global.json not found (optional)" -ForegroundColor Gray
}

# 3. Восстанавливаем зависимости
Write-Host "[3/4] Restoring dependencies..." -ForegroundColor Yellow
dotnet restore "$script:repoRoot\konserva-app\konserva-app.csproj" --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Dependencies restored" -ForegroundColor Green

# 4. Сборка и тесты
Write-Host "[4/4] Building and testing..." -ForegroundColor Yellow
dotnet build "$script:repoRoot\konserva-app\konserva-app.csproj" --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded" -ForegroundColor Green

dotnet test "$script:repoRoot\konserva-app.Tests\konserva-app.Tests.csproj" --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Some tests failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Setup complete! ===" -ForegroundColor Cyan
Write-Host "Open the project in VS Code and press F5 to run." -ForegroundColor White
