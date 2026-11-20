<#
.SYNOPSIS
    Verificação E2E das telas React + ponte nativa via CDP do WebView2.

.DESCRIPTION
    Fluxo completo em um comando só:
      1. build da UI (ui/ -> wwwroot)
      2. build do Verso.App
      3. seed da transcrição de teste (editor)
      4. sobe o app com --remote-debugging-port=9223 (ou reusa se o CDP já está no ar)
      5. roda scripts/verify-bridge.mjs (checks de ponte + DOM de todas as telas)

    ATENÇÃO: os checks de editor (saveText/split/merge/updateMeta) MUTAM a
    transcrição semeada — por isso o seed roda antes de cada verificação.

.PARAMETER SkipBuild
    Pula os builds (útil para iterar só no script de verificação).

.PARAMETER RestartApp
    Mata um Verso.App já rodando e reinicia com CDP (sem isso, um app aberto
    sem a porta de debug faz o script parar com instrução).

.EXAMPLE
    ./scripts/run-ui-checks.ps1
    ./scripts/run-ui-checks.ps1 -SkipBuild -RestartApp
#>
param(
    [switch]$SkipBuild,
    [switch]$RestartApp
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Test-Cdp {
    try {
        Invoke-RestMethod -Uri "http://127.0.0.1:9223/json" -TimeoutSec 2 | Out-Null
        return $true
    } catch {
        return $false
    }
}

if (-not $SkipBuild) {
    Write-Host "==> Build da UI (React -> wwwroot)" -ForegroundColor Cyan
    Push-Location "$root\ui"
    try { npm run build } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "==> Build do Verso.App" -ForegroundColor Cyan
    dotnet build "$root\src\Verso.App\Verso.App.csproj" -c Debug -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "==> Seed da transcrição de teste" -ForegroundColor Cyan
python "$root\scripts\seed-test-transcription.py"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = "$root\src\Verso.App\bin\Debug\net10.0\Verso.App.exe"
$appRunning = [bool](Get-Process -Name "Verso.App" -ErrorAction SilentlyContinue)

if (Test-Cdp) {
    Write-Host "==> CDP já disponível — reusando app em execução" -ForegroundColor Cyan
} elseif ($appRunning -and -not $RestartApp) {
    Write-Host "" 
    Write-Host "O Verso.App já está rodando SEM a porta de debug (CDP :9223)." -ForegroundColor Yellow
    Write-Host "Feche o app e rode de novo, ou use -RestartApp para reiniciá-lo." -ForegroundColor Yellow
    exit 1
} else {
    if ($appRunning) {
        Write-Host "==> Reiniciando Verso.App com CDP" -ForegroundColor Cyan
        Stop-Process -Name "Verso.App" -Force
        Start-Sleep -Seconds 2
    } else {
        Write-Host "==> Iniciando Verso.App com CDP :9223" -ForegroundColor Cyan
    }
    # O processo filho herda o ambiente desta sessão.
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=9223"
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
    $ready = $false
    foreach ($i in 1..30) {
        Start-Sleep -Seconds 1
        if (Test-Cdp) { $ready = $true; break }
    }
    if (-not $ready) {
        Write-Error "CDP não respondeu em 30s — verifique se o app subiu."
        exit 1
    }
}

Write-Host "==> verify-bridge.mjs (ponte + telas)" -ForegroundColor Cyan
node "$root\scripts\verify-bridge.mjs"
exit $LASTEXITCODE
