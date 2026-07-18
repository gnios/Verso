$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> Building Verso.App" -ForegroundColor Cyan
dotnet build "$root\src\Verso.App\Verso.App.csproj" -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Running Verso.E2E" -ForegroundColor Cyan
dotnet test "$root\tests\Verso.E2E\Verso.E2E.csproj" -c Debug -v n
exit $LASTEXITCODE
