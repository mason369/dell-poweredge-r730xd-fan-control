param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ExeOutputDirectory = "artifacts\exe\win-x64",
    [string]$ReleaseOutputDirectory = "artifacts\release",
    [string]$ZipName = "DellR730xdFanControlCenter-win-x64.zip",
    [switch]$VerifyLaunch
)

$ErrorActionPreference = "Stop"

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

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Release zip verification failed: missing '$RelativePath' under '$Root'."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishExeScript = Join-Path $repoRoot "tools\Publish-UnpackagedExe.ps1"
$resolvedExeOutputDirectory = Join-Path $repoRoot $ExeOutputDirectory
$resolvedReleaseOutputDirectory = Join-Path $repoRoot $ReleaseOutputDirectory
$zipPath = Join-Path $resolvedReleaseOutputDirectory $ZipName
$verificationDirectory = Join-Path $resolvedReleaseOutputDirectory "zip-verification"

& $publishExeScript `
    -Configuration $Configuration `
    -Platform $Platform `
    -RuntimeIdentifier $RuntimeIdentifier `
    -OutputDirectory $ExeOutputDirectory | Out-Host

New-Item -ItemType Directory -Path $resolvedReleaseOutputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction Stop
}

Compress-Archive -Path (Join-Path $resolvedExeOutputDirectory "*") -DestinationPath $zipPath -Force
if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Release zip was not created at '$zipPath'."
}

Remove-DirectoryIfPresent -Path $verificationDirectory -Description "previous release zip verification directory"
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null

try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $verificationDirectory -Force

    $requiredPaths = @(
        "DellR730xdFanControlCenter.exe",
        "DellR730xdFanControlCenter.dll",
        "DellR730xdFanControlCenter.runtimeconfig.json",
        "Microsoft.WindowsAppRuntime.dll",
        "Microsoft.ui.xaml.dll",
        "DellR730xdFanControlCenter.pri",
        "Assets\AppIcon.ico",
        "Assets\Charts\dashboard.html",
        "Assets\Charts\echarts.min.js",
        "BundledTools\ipmitool\ipmitool.exe"
    )

    foreach ($relativePath in $requiredPaths) {
        Assert-RequiredFile -Root $verificationDirectory -RelativePath $relativePath
    }

    $forbiddenReleaseFiles = Get-ChildItem -LiteralPath $verificationDirectory -Recurse -File |
        Where-Object {
            $_.Extension -in @(".msix", ".pfx", ".cer") -or
            $_.Name -in @("AppxManifest.xml", "Package.appxmanifest")
        }

    if ($forbiddenReleaseFiles) {
        $fileList = ($forbiddenReleaseFiles | ForEach-Object { $_.FullName.Substring($verificationDirectory.Length).TrimStart([char[]]@("\", "/")) }) -join ", "
        throw "Release zip verification failed: signed/package-identity files are not allowed in the unsigned downloadable zip: $fileList"
    }

    if ($VerifyLaunch) {
        $existingProcesses = Get-Process DellR730xdFanControlCenter -ErrorAction SilentlyContinue
        if ($existingProcesses) {
            $processList = ($existingProcesses | ForEach-Object { "$($_.Id): $($_.Path)" }) -join "; "
            throw "Close running DellR730xdFanControlCenter processes before using -VerifyLaunch: $processList"
        }

        $exePath = Join-Path $verificationDirectory "DellR730xdFanControlCenter.exe"
        $startedAt = Get-Date
        $process = Start-Process -FilePath $exePath -PassThru
        $runningProcess = $null

        try {
            $deadline = (Get-Date).AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 500
                $runningProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
                if ($runningProcess -and $runningProcess.MainWindowHandle -ne 0) {
                    break
                }
            } while ((Get-Date) -lt $deadline)

            if (-not $runningProcess) {
                throw "The extracted release exe exited before a window was detected."
            }

            if ($runningProcess.MainWindowHandle -eq 0) {
                throw "The extracted release exe started but did not create a top-level window within 20 seconds."
            }

            if ([string]::IsNullOrWhiteSpace($runningProcess.MainWindowTitle)) {
                throw "The extracted release exe created a window without a title."
            }

            $startupErrors = Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = $startedAt } -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.ProviderName -in @("Application Error", ".NET Runtime") -and
                    $_.Message -like "*DellR730xdFanControlCenter*"
                }

            if ($startupErrors) {
                $messages = ($startupErrors | Select-Object -First 3 | ForEach-Object { "$($_.TimeCreated): $($_.ProviderName) $($_.Id) $($_.Message)" }) -join "`n"
                throw "The extracted release exe logged startup errors:`n$messages"
            }
        }
        finally {
            $launchedProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($launchedProcess) {
                Stop-Process -Id $launchedProcess.Id -Force -ErrorAction Stop
            }
        }
    }
}
finally {
    Remove-DirectoryIfPresent -Path $verificationDirectory -Description "release zip verification directory"
}

[pscustomobject]@{
    Zip = $zipPath
    SourceDirectory = $resolvedExeOutputDirectory
    VerifiedLaunch = [bool]$VerifyLaunch
}
