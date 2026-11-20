param(
    [Parameter(Mandatory, Position = 0)]
    [string[]] $Path,

    [switch] $KeepValidSignatures
)

$ErrorActionPreference = 'Stop'
$files = $Path | ForEach-Object { (Get-Item -LiteralPath $_).FullName }
$toolPath = (Resolve-Path -LiteralPath $env:VERSO_CODESIGN_TOOL).Path
$stage = New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) ('verso-sign-' + [guid]::NewGuid()))
$outputDir = New-Item -ItemType Directory (Join-Path $stage.FullName 'signed')
$inputFile = $null
$signedFile = $null

Push-Location -LiteralPath $toolPath
try {
    foreach ($file in $files) {
        if ($KeepValidSignatures) {
            $existing = Get-AuthenticodeSignature -LiteralPath $file
            if ($existing.Status -eq 'Valid' -and $existing.SignatureType -eq 'Authenticode' -and $existing.TimeStamperCertificate) {
                Write-Host "Assinatura existente verificada em '$file' ($($existing.SignerCertificate.Subject))."
                continue
            }
        }

        # O Inno usa extensões temporárias. O eSigner identifica o formato pelo nome.
        $basename = if ([IO.Path]::GetExtension($file) -eq '.dll') { 'Verso.dll' } else { 'Verso.exe' }
        $inputFile = Join-Path $stage.FullName $basename
        $signedFile = Join-Path $outputDir.FullName $basename
        Copy-Item -LiteralPath $file -Destination $inputFile -Force
        $signingArgs = @(
            '-jar', 'jar/code_sign_tool-1.3.2.jar', 'sign',
            "-username=$env:ESIGNER_USERNAME",
            "-password=$env:ESIGNER_PASSWORD",
            "-credential_id=$env:ESIGNER_CREDENTIAL_ID",
            "-totp_secret=$env:ESIGNER_TOTP_SECRET",
            "-input_file_path=$inputFile",
            "-output_dir_path=$($outputDir.FullName)"
        )
        & java @signingArgs
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $signedFile -PathType Leaf)) {
            throw "Falha do eSigner ao assinar '$file' (código $LASTEXITCODE)."
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $signedFile
        if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or -not $signature.TimeStamperCertificate) {
            throw "Assinatura Authenticode inválida ou sem carimbo de tempo em '$file' ($($signature.Status))."
        }
        Move-Item -LiteralPath $signedFile -Destination $file -Force
        Remove-Item -LiteralPath $inputFile -Force
        Write-Host "Assinatura verificada em '$file' ($($signature.SignerCertificate.Subject))."
    }
} finally {
    Pop-Location
    if ($inputFile) { Remove-Item -LiteralPath $inputFile, $signedFile -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $outputDir.FullName, $stage.FullName -Force
}
