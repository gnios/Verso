$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$uiPath = Join-Path $repoRoot 'ui'
$appProject = Join-Path $repoRoot 'src/Verso.App'
$appExecutable = [IO.Path]::GetFullPath((Join-Path $appProject 'bin/Debug/net10.0/Verso.App.exe'))

if (-not (Test-Path (Join-Path $uiPath 'node_modules/.bin/vite.cmd'))) {
    npm.cmd --prefix $uiPath ci
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar as dependencias do frontend.' }
}

npm.cmd --prefix $uiPath run build
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o frontend.' }

foreach ($app in (Get-Process -Name Verso.App -ErrorAction SilentlyContinue | Where-Object Path -eq $appExecutable)) {
    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(5000)) {
        throw 'Feche a janela do Verso deste workspace antes de executar o script novamente.'
    }
}

dotnet build $appProject --configuration Debug
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o Verso.' }

Start-Process -FilePath $appExecutable -WorkingDirectory (Split-Path -Parent $appExecutable)
