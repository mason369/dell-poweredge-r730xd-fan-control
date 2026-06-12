param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Subject = "CN=mason369",
    [string]$OutputDirectory = "artifacts\msix",
    [switch]$SkipTrustImport
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "DellR730xdFanControlCenter.csproj"
$manifestPath = Join-Path $repoRoot "Package.appxmanifest"
$certificateDirectory = Join-Path $repoRoot "artifacts\certificates"
$certificatePath = Join-Path $certificateDirectory "mason369-msix-signing.cer"
$resolvedOutputDirectory = Join-Path $repoRoot $OutputDirectory
$intermediateDirectory = Join-Path $repoRoot "obj\signed-msix"

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$manifestPublisher = $manifest.Package.Identity.Publisher
if ($manifestPublisher -ne $Subject) {
    throw "Package.appxmanifest Publisher is '$manifestPublisher', but the signing certificate subject is '$Subject'. They must match for MSIX signing."
}

$codeSigningOid = "1.3.6.1.5.5.7.3.3"
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $Subject -and
        $_.HasPrivateKey -and
        ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid })
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName "DellR730xdFanControlCenter MSIX Signing" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(3)
}

New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

if (-not $SkipTrustImport) {
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null

$msbuildArguments = @(
    "msbuild",
    $projectPath,
    "/restore",
    "/t:Publish",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:RuntimeIdentifier=$RuntimeIdentifier",
    "/p:GenerateAppxPackageOnBuild=true",
    "/p:AppxPackageSigningEnabled=true",
    "/p:PackageCertificateThumbprint=$($certificate.Thumbprint)",
    "/p:UapAppxPackageBuildMode=SideloadOnly",
    "/p:AppxBundle=Never",
    "/p:AppxPackageDir=$resolvedOutputDirectory\",
    "/p:SelfContained=true",
    "/p:PublishSingleFile=false",
    "/p:PublishTrimmed=false",
    "/p:BaseIntermediateOutputPath=$intermediateDirectory\"
)

& dotnet @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "MSIX publish failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem -LiteralPath $resolvedOutputDirectory -Recurse -Filter "DellR730xdFanControlCenter_*.msix" |
    Where-Object { $_.Name -notlike "Microsoft.WindowsAppRuntime*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "MSIX publish completed but no DellR730xdFanControlCenter_*.msix package was found under '$resolvedOutputDirectory'."
}

$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
if ($signature.Status -ne "Valid") {
    throw "MSIX signature verification failed: $($signature.Status) - $($signature.StatusMessage)"
}

[pscustomobject]@{
    Package = $package.FullName
    PublicCertificate = $certificatePath
    Thumbprint = $certificate.Thumbprint
    SignatureStatus = $signature.Status
}
