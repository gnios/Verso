param(
    [Parameter(Mandatory)]
    [string] $MakeAppx,

    [ValidatePattern('^[1-9]\d*\.\d+\.\d+\.0$')]
    [version] $Version = '1.0.0.0'
)

$ErrorActionPreference = 'Stop'
if ($Version.Major -gt 65535 -or $Version.Minor -gt 65535 -or $Version.Build -gt 65535) {
    throw 'Cada componente da versão MSIX deve caber em 16 bits.'
}

$packer = (Resolve-Path -LiteralPath $MakeAppx).Path
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/store'
$stage = Join-Path $output ('stage-' + [guid]::NewGuid())
$package = Join-Path $output "Verso-Transcricao-$Version-x64.msix"

dotnet publish (Join-Path $repo 'src/Verso.App/Verso.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:VersoIncludeGpuRuntimes=false -p:VersoStorePackage=true "-p:Version=$Version" -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)." }

foreach ($required in @('Verso.App.exe', 'engine/Verso.Worker.exe', 'engine/wwwroot/index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) {
        throw "Arquivo ausente no pacote: $required"
    }
}
if (-not (Get-ChildItem -LiteralPath (Join-Path $stage 'engine') -Recurse -File -Filter 'e_sqlite3.dll')) {
    throw 'SQLite nativo ausente no pacote.'
}

$manifest = [xml](Get-Content -Raw -Encoding UTF8 (Join-Path $repo 'installer/windows/store/AppxManifest.xml'))
$manifest.Package.Identity.Version = $Version.ToString()
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))

Add-Type -AssemblyName System.Drawing
$assets = New-Item -ItemType Directory (Join-Path $stage 'Assets')
$logo = [System.Drawing.Image]::FromFile((Join-Path $repo 'src/Verso.App/Assets/verso-logo.png'))
try {
    $logos = @{ StoreLogo = 50; Square44x44Logo = 44; Square150x150Logo = 150 }
    foreach ($entry in $logos.GetEnumerator()) {
        $size = $entry.Value
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scale = $size / [Math]::Max($logo.Width, $logo.Height)
            $width = [int]($logo.Width * $scale)
            $height = [int]($logo.Height * $scale)
            $graphics.DrawImage($logo, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $bitmap.Save((Join-Path $assets.FullName ($entry.Key + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
} finally {
    $logo.Dispose()
}

$mapping = "$stage.map"
$fileMappings = foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
    $relative = $file.FullName.Substring($stage.Length + 1)
    if ($relative -match '^engine\\runtimes\\(?!win-x64\\|noavx\\win-x64\\)' `
        -or $file.Extension -in '.lib', '.metal' -or $file.Name -like 'Verso.Updater.*') {
        continue
    }
    '"{0}" "{1}"' -f $file.FullName, $relative
}
Set-Content -LiteralPath $mapping -Value (@('[Files]') + $fileMappings) -Encoding Unicode
& $packer pack /o /f $mapping /p $package
if ($LASTEXITCODE -ne 0) { throw "MakeAppx falhou ($LASTEXITCODE)." }
Write-Host "Pacote para o Partner Center: $package"
