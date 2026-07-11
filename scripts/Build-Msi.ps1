[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = "1.0.0.0",
    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\release",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repoRoot "WordReviewReminder\WordReviewReminder.csproj"
$installer = Join-Path $repoRoot "installer\Product.wxs"
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$parsedVersion = [Version]$Version
$msiVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build)"
$publish = Join-Path $repoRoot "artifacts\intermediate\msi-$msiVersion-$PID"
$msiPath = Join-Path $output "WordReviewReminder-x64.msi"

New-Item -ItemType Directory -Path $publish, $output -Force | Out-Null

& dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "WiX tool restore failed with exit code $LASTEXITCODE."
}

$publishArguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-o", $publish,
    "-p:Platform=x64",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:GenerateAppxPackageOnBuild=false",
    "-p:DebugSymbols=false",
    "-p:DebugType=None"
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "MSI application publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath (Join-Path $publish "WordReviewReminder.exe"))) {
    throw "The MSI publish output does not contain WordReviewReminder.exe."
}

& dotnet wix build $installer `
    -arch x64 `
    -d "ProductVersion=$msiVersion" `
    -d "PublishDir=$publish" `
    -pdbtype none `
    -out $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX MSI build failed with exit code $LASTEXITCODE."
}

$signTool = Get-ChildItem `
    -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" `
    -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signTool) {
    throw "SignTool.exe was not found. Install the Windows SDK signing tools."
}

& $signTool.FullName sign `
    /fd SHA256 `
    /sha1 $CertificateThumbprint `
    /s My `
    /tr $TimestampUrl `
    /td SHA256 `
    /d "Word Review Reminder" `
    $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "MSI signing failed with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -FilePath $msiPath
if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw "The generated MSI is missing the expected code signature."
}

Write-Host "Signed MSI ready: $msiPath"
Get-Item -LiteralPath $msiPath | Select-Object Name, Length, LastWriteTime
