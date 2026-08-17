# Move publish payload into PublishDir/engine, leaving only Verso.App(.exe) at the root.
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir
)

$ErrorActionPreference = 'Stop'

$pub = [System.IO.Path]::GetFullPath($PublishDir).TrimEnd('\', '/')
$engine = Join-Path $pub 'engine'
New-Item -ItemType Directory -Force -Path $engine | Out-Null

$keep = @('Verso.App.exe', 'Verso.App', 'engine', 'data')

Get-ChildItem -LiteralPath $pub -Force | Where-Object { $_.Name -notin $keep } | ForEach-Object {
    $dest = Join-Path $engine $_.Name
    if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Recurse -Force
    }
    Move-Item -LiteralPath $_.FullName -Destination $dest -Force
}

# Símbolos de debug não vão no pacote portátil.
Get-ChildItem -LiteralPath $pub -Recurse -Force -Filter '*.pdb' -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (-not (Test-Path (Join-Path $engine 'wwwroot'))) {
    throw 'LayoutPayloadDirectory: wwwroot ausente em engine/'
}
if (-not (Test-Path (Join-Path $engine 'runtimes'))) {
    throw 'LayoutPayloadDirectory: runtimes ausente em engine/'
}

Write-Host "LayoutPayloadDirectory: payload em $engine"
