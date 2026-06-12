param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "artifacts\exe\win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "DellR730xdFanControlCenter.csproj"
$resolvedOutputDirectory = Join-Path $repoRoot $OutputDirectory
$intermediateDirectory = Join-Path $repoRoot "obj\unpackaged-exe"

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null

$runningFromOutput = Get-Process DellR730xdFanControlCenter -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$resolvedOutputDirectory*" }
if ($runningFromOutput) {
    $processList = ($runningFromOutput | ForEach-Object { "$($_.Id): $($_.Path)" }) -join "; "
    throw "Cannot publish to '$resolvedOutputDirectory' while the published app is running. Close these processes first: $processList"
}

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
    "Assets\AppIcon.ico",
    "Assets\Charts\dashboard.html",
    "Assets\Charts\echarts.min.js",
    "BundledTools\ipmitool\ipmitool.exe"
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
