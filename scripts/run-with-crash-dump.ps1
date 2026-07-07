<#
.SYNOPSIS
    Roda o Transcriba.App com coleta automática de memory dump nativo caso o
    "Internal CLR error (0x80131506)" ocorra de novo.

.DESCRIPTION
    Os stack traces gerenciados desse crash mostram só ONDE a corrupção de
    memória foi *percebida* (normalmente dentro do Avalonia), não onde ela
    foi *causada*. Para achar a causa raiz de verdade, precisamos de um
    memory dump completo do processo no momento da falha, analisado com
    `dotnet-dump analyze` (ou WinDbg + SOS).

    Este script configura as variáveis de ambiente do runtime do .NET que
    fazem o próprio CLR gravar um dump automaticamente quando esse tipo de
    erro fatal acontece, e então builda + roda o app.

.EXAMPLE
    ./scripts/run-with-crash-dump.ps1
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dumpDir = Join-Path $env:TEMP 'transcriba-dumps'
New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null

# Faz o runtime gravar um minidump completo (heap + módulos nativos) quando
# ocorrer um erro fatal do CLR, sem precisar de ferramenta externa (ProcDump/WER).
$env:DOTNET_DbgEnableMiniDump = '1'
$env:DOTNET_DbgMiniDumpType = '4'        # 4 = MiniDumpWithFullMemory (dump completo)
$env:DOTNET_DbgMiniDumpName = (Join-Path $dumpDir 'transcriba-crash-%p.dmp')
$env:DOTNET_CreateDumpVerboseDiagnostics = '1'
$env:DOTNET_CreateDumpDiagnostics = '1'

Write-Host "Dumps serão salvos em: $dumpDir" -ForegroundColor Cyan
Write-Host "Rodando Transcriba.App com coleta de dump habilitada..." -ForegroundColor Cyan

Push-Location (Join-Path $repoRoot 'src/Transcriba.App')
try
{
    dotnet run --no-launch-profile
}
finally
{
    Pop-Location
    Write-Host ""
    Write-Host "Se o app crashou, procure o(s) arquivo(s) .dmp em: $dumpDir" -ForegroundColor Yellow
    Write-Host "Para analisar: dotnet-dump analyze <caminho-do-dump>" -ForegroundColor Yellow
}
