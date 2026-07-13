param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Subject = "CN=mason369",
    [string]$OutputDirectory = "artifacts\msix",
    [switch]$SkipTrustImport
)

$ErrorActionPreference = "Stop"

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $targetPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = "$rootPath$([System.IO.Path]::DirectorySeparatorChar)"
    if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must stay under repository root '$rootPath'; resolved path was '$targetPath'."
    }
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-CertificateInStores {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string[]]$Stores
    )

    foreach ($store in $Stores) {
        $trustedCertificate = Get-ChildItem -LiteralPath $store -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1

        if (-not $trustedCertificate) {
            throw "MSIX signing certificate '$Thumbprint' is missing from '$store'. Windows deployment may reject the package with 0x800B0109."
        }
    }
}

function Remove-DirectoryIfPresent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Failed to remove $Description at '$Path'. Close any running process that is using that directory and run the publish script again."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "DellR730xdFanControlCenter.csproj"
$manifestPath = Join-Path $repoRoot "Package.appxmanifest"
$certificateDirectory = Join-Path $repoRoot "artifacts\certificates"
$certificatePath = Join-Path $certificateDirectory "mason369-msix-signing.cer"
$resolvedOutputDirectory = Join-Path $repoRoot $OutputDirectory
$intermediateDirectory = Join-Path $repoRoot "obj\signed-msix"
$msixPublishDirectory = Join-Path $intermediateDirectory "publish"

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$manifestPublisher = $manifest.Package.Identity.Publisher
if ($manifestPublisher -ne $Subject) {
    throw "Package.appxmanifest Publisher is '$manifestPublisher', but the signing certificate subject is '$Subject'. They must match for MSIX signing."
}

[xml]$project = Get-Content -LiteralPath $projectPath
$targetFramework = $project.Project.PropertyGroup |
    ForEach-Object { $_.TargetFramework } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if (-not $targetFramework) {
    throw "Could not determine TargetFramework from '$projectPath'."
}

$legacyPackagedPublishDirectory = Join-Path $repoRoot "bin\$Configuration\$targetFramework\$RuntimeIdentifier\publish"

Assert-PathUnderRoot -Path $resolvedOutputDirectory -Root $repoRoot -Description "Signed MSIX output directory"
Assert-PathUnderRoot -Path $intermediateDirectory -Root $repoRoot -Description "Signed MSIX intermediate directory"
Assert-PathUnderRoot -Path $legacyPackagedPublishDirectory -Root $repoRoot -Description "Legacy packaged publish directory"

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
    if (-not (Test-IsElevated)) {
        throw "MSIX local installation requires the signing certificate in the LocalMachine trust stores. Re-run this script from an elevated PowerShell session, or pass -SkipTrustImport only when the target machine already trusts the signing certificate."
    }

    $trustedStores = @(
        "Cert:\CurrentUser\TrustedPeople",
        "Cert:\CurrentUser\Root",
        "Cert:\LocalMachine\TrustedPeople",
        "Cert:\LocalMachine\Root"
    )

    foreach ($store in $trustedStores) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation $store | Out-Null
    }

    Assert-CertificateInStores -Thumbprint $certificate.Thumbprint -Stores $trustedStores
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null
Remove-DirectoryIfPresent -Path $msixPublishDirectory -Description "previous MSIX publish intermediate directory"

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
    "/p:WindowsAppSDKSelfContained=true",
    "/p:PublishSingleFile=false",
    "/p:PublishTrimmed=false",
    "/p:PublishDir=$msixPublishDirectory\",
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

$inspectionDirectory = Join-Path $resolvedOutputDirectory "_package-inspection"
$inspectionArchivePath = Join-Path $resolvedOutputDirectory "_package-inspection.zip"
Remove-DirectoryIfPresent -Path $inspectionDirectory -Description "previous MSIX inspection directory"
if (Test-Path -LiteralPath $inspectionArchivePath) {
    Remove-Item -LiteralPath $inspectionArchivePath -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $inspectionDirectory -Force | Out-Null
try {
    Copy-Item -LiteralPath $package.FullName -Destination $inspectionArchivePath -Force -ErrorAction Stop
    Expand-Archive -LiteralPath $inspectionArchivePath -DestinationPath $inspectionDirectory -Force
    $generatedManifestPath = Join-Path $inspectionDirectory "AppxManifest.xml"
    if (-not (Test-Path -LiteralPath $generatedManifestPath)) {
        throw "MSIX package is missing AppxManifest.xml."
    }

    [xml]$generatedManifest = Get-Content -LiteralPath $generatedManifestPath
    $packageDependencies = $generatedManifest.Package.Dependencies.PackageDependency
    if ($packageDependencies) {
        $dependencyList = ($packageDependencies | ForEach-Object { $_.Name }) -join ", "
        throw "MSIX package still declares external package dependencies: $dependencyList. The package must be Windows App SDK self-contained for this release script."
    }

    $requiredPackagePaths = @(
        "DellR730xdFanControlCenter.exe",
        "Microsoft.WindowsAppRuntime.dll",
        "Microsoft.ui.xaml.dll",
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "Assets\AppIcon.ico",
        "Assets\Charts\dashboard.html",
        "Assets\Charts\echarts.min.js",
        "Assets\Charts\echarts.LICENSE.txt",
        "Assets\Charts\echarts.NOTICE.txt",
        "BundledTools\ipmitool\ipmitool.exe",
        "BundledTools\ipmitool\README.md",
        "BundledTools\ipmitool\LICENSES\ipmitool-BSD.txt",
        "BundledTools\ipmitool\LICENSES\cygwin-LICENSE-NOTICE.txt",
        "BundledTools\ipmitool\LICENSES\openssl-102n-LICENSE.txt",
        "BundledTools\ipmitool\LICENSES\gcc-runtime-exception-NOTICE.txt",
        "BundledTools\ipmitool\LICENSES\zlib-128-LICENSE.txt"
    )

    foreach ($relativePath in $requiredPackagePaths) {
        $path = Join-Path $inspectionDirectory $relativePath
        if (-not (Test-Path -LiteralPath $path)) {
            throw "MSIX package is missing required runtime file: $relativePath"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $inspectionArchivePath) {
        Remove-Item -LiteralPath $inspectionArchivePath -Force -ErrorAction Stop
    }

    Remove-DirectoryIfPresent -Path $inspectionDirectory -Description "MSIX inspection directory"
}

Remove-DirectoryIfPresent -Path $msixPublishDirectory -Description "MSIX publish intermediate directory"
Remove-DirectoryIfPresent -Path $legacyPackagedPublishDirectory -Description "legacy bin publish byproduct"

[pscustomobject]@{
    Package = $package.FullName
    PublicCertificate = $certificatePath
    Thumbprint = $certificate.Thumbprint
    SignatureStatus = $signature.Status
}
