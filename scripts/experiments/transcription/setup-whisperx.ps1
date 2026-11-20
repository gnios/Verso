# Instala ambiente WhisperX com Python 3.12 (WhisperX nao suporta Python 3.14)
$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
    Write-Error "Python launcher (py) nao encontrado."
}

$py312 = py -3.12 -c "import sys; print(sys.executable)" 2>$null
if (-not $py312) {
    Write-Host "Python 3.12 nao encontrado. Instalando..."
    winget install Python.Python.3.12 --accept-package-agreements --accept-source-agreements
}

Write-Host "Criando ambiente virtual .venv-whisperx ..."
py -3.12 -m venv .venv-whisperx

Write-Host "Instalando dependencias ..."
.\.venv-whisperx\Scripts\python.exe -m pip install --upgrade pip
.\.venv-whisperx\Scripts\pip.exe install -r requirements-whisperx.txt

Write-Host "Instalando PyTorch com CUDA ..."
.\.venv-whisperx\Scripts\pip.exe install --force-reinstall torch==2.8.0 torchaudio==2.8.0 torchvision==0.23.0 --index-url https://download.pytorch.org/whl/cu128

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    Write-Host "Instalando ffmpeg ..."
    winget install Gyan.FFmpeg --accept-package-agreements --accept-source-agreements
    Write-Host "Reabra o terminal apos a instalacao do ffmpeg."
}

Write-Host ""
Write-Host "Pronto! Para usar:"
Write-Host "  .\.venv-whisperx\Scripts\Activate.ps1"
Write-Host "  python transcrever_x.py"
