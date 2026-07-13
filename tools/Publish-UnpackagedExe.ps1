param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "artifacts\exe\win-x64"
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
        throw "Failed to remove $Description at '$Path'. Close any process using that directory and run the publish script again."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "DellR730xdFanControlCenter.csproj"
$resolvedOutputDirectory = Join-Path $repoRoot $OutputDirectory
$intermediateDirectory = Join-Path $repoRoot "obj\unpackaged-exe"

Assert-PathUnderRoot -Path $resolvedOutputDirectory -Root $repoRoot -Description "Unpackaged exe output directory"
Assert-PathUnderRoot -Path $intermediateDirectory -Root $repoRoot -Description "Unpackaged exe intermediate directory"

$runningFromOutput = Get-Process DellR730xdFanControlCenter -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$resolvedOutputDirectory*" }
if ($runningFromOutput) {
    $processList = ($runningFromOutput | ForEach-Object { "$($_.Id): $($_.Path)" }) -join "; "
    throw "Cannot publish to '$resolvedOutputDirectory' while the published app is running. Close these processes first: $processList"
}

Remove-DirectoryIfPresent -Path $resolvedOutputDirectory -Description "unpackaged exe output directory"
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null

$publishArguments = @(
    "publish",
    $projectPath,
    "-c",
    $Configuration,
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=$RuntimeIdentifier",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:SelfContained=true",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:BaseIntermediateOutputPath=$intermediateDirectory\",
    "-o",
    $resolvedOutputDirectory
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Unpackaged exe publish failed with exit code $LASTEXITCODE."
}

$requiredPaths = @(
    "DellR730xdFanControlCenter.exe",
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

foreach ($relativePath in $requiredPaths) {
    $path = Join-Path $resolvedOutputDirectory $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Published exe output is missing required file: $path"
    }
}

[pscustomobject]@{
    Executable = Join-Path $resolvedOutputDirectory "DellR730xdFanControlCenter.exe"
    OutputDirectory = $resolvedOutputDirectory
}
