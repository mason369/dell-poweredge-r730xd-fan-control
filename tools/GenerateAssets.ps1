Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeIconMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $projectRoot "Assets"

function New-Canvas {
    param([int]$Width, [int]$Height)
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    return @($bitmap, $graphics)
}

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Rect, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Logo {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.RectangleF]$Bounds,
        [bool]$WithWordmark = $false
    )

    $bg = [System.Drawing.ColorTranslator]::FromHtml("#10202F")
    $panel = [System.Drawing.ColorTranslator]::FromHtml("#152C3C")
    $teal = [System.Drawing.ColorTranslator]::FromHtml("#5EEAD4")
    $fan = [System.Drawing.ColorTranslator]::FromHtml("#0F766E")
    $blade = [System.Drawing.ColorTranslator]::FromHtml("#67E8F9")
    $white = [System.Drawing.ColorTranslator]::FromHtml("#F8FAFC")
    $amber = [System.Drawing.ColorTranslator]::FromHtml("#FBBF24")
    $blue = [System.Drawing.ColorTranslator]::FromHtml("#93C5FD")

    $Graphics.Clear([System.Drawing.Color]::Transparent)

    $iconSize = [Math]::Min($Bounds.Width, $Bounds.Height) * $(if ($WithWordmark) { 0.52 } else { 0.88 })
    $x = $Bounds.X + 0.08 * $Bounds.Width
    $y = $Bounds.Y + ($Bounds.Height - $iconSize) / 2
    $icon = [System.Drawing.RectangleF]::new($x, $y, $iconSize, $iconSize)

    $outer = New-RoundedPath $icon ($iconSize * 0.18)
    $Graphics.FillPath((New-Object System.Drawing.SolidBrush($bg)), $outer)

    $server = [System.Drawing.RectangleF]::new($icon.X + $iconSize * 0.13, $icon.Y + $iconSize * 0.22, $iconSize * 0.66, $iconSize * 0.52)
    $serverPath = New-RoundedPath $server ($iconSize * 0.06)
    $Graphics.FillPath((New-Object System.Drawing.SolidBrush($panel)), $serverPath)
    $Graphics.DrawPath((New-Object System.Drawing.Pen($teal, [Math]::Max(3, $iconSize * 0.028))), $serverPath)

    for ($i = 0; $i -lt 3; $i++) {
        $rack = [System.Drawing.RectangleF]::new($server.X + $iconSize * 0.06, $server.Y + $iconSize * (0.08 + 0.13 * $i), $iconSize * 0.24, $iconSize * 0.07)
        $brushColor = @($white, $blue, $amber)[$i]
        $Graphics.FillRectangle((New-Object System.Drawing.SolidBrush($brushColor)), $rack)
    }

    $cx = $server.X + $server.Width * 0.76
    $cy = $server.Y + $server.Height * 0.5
    $r = $iconSize * 0.16
    $Graphics.FillEllipse((New-Object System.Drawing.SolidBrush($fan)), $cx - $r, $cy - $r, $r * 2, $r * 2)
    $Graphics.DrawEllipse((New-Object System.Drawing.Pen($white, [Math]::Max(2, $iconSize * 0.02))), $cx - $r, $cy - $r, $r * 2, $r * 2)

    for ($i = 0; $i -lt 4; $i++) {
        $Graphics.TranslateTransform($cx, $cy)
        $Graphics.RotateTransform($i * 90)
        $bladePath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $bladePath.AddBezier(0, -$r * 0.25, $r * 0.44, -$r * 0.8, $r * 0.18, -$r * 1.02, -$r * 0.04, -$r * 0.3)
        $bladePath.CloseFigure()
        $Graphics.FillPath((New-Object System.Drawing.SolidBrush($blade)), $bladePath)
        $Graphics.ResetTransform()
    }

    $hub = $r * 0.22
    $Graphics.FillEllipse((New-Object System.Drawing.SolidBrush($white)), $cx - $hub, $cy - $hub, $hub * 2, $hub * 2)
    $Graphics.DrawLine((New-Object System.Drawing.Pen($amber, [Math]::Max(3, $iconSize * 0.035))), $server.X, $server.Bottom + $iconSize * 0.1, $server.Right, $server.Bottom + $iconSize * 0.1)

    if ($WithWordmark) {
        $fontTitle = New-Object System.Drawing.Font("Segoe UI Variable Display", [Math]::Max(24, $Bounds.Height * 0.12), [System.Drawing.FontStyle]::Bold)
        $fontSub = New-Object System.Drawing.Font("Segoe UI", [Math]::Max(12, $Bounds.Height * 0.045), [System.Drawing.FontStyle]::Regular)
        $textBrush = New-Object System.Drawing.SolidBrush($white)
        $subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#CFFAFE"))
        $tx = $icon.Right + $Bounds.Width * 0.05
        $Graphics.DrawString("R730XD Fan Control", $fontTitle, $textBrush, $tx, $Bounds.Height * 0.30)
        $Graphics.DrawString("iDRAC Smart Cooling Center", $fontSub, $subBrush, $tx, $Bounds.Height * 0.54)
    }
}

function Save-LogoPng {
    param([string]$Name, [int]$Width, [int]$Height, [bool]$Wordmark = $false)
    $canvas = New-Canvas $Width $Height
    $bitmap = $canvas[0]
    $graphics = $canvas[1]
    Draw-Logo $graphics ([System.Drawing.RectangleF]::new(0, 0, $Width, $Height)) $Wordmark
    $target = Join-Path $assets $Name
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Save-AppIcon {
    $canvas = New-Canvas 256 256
    $bitmap = $canvas[0]
    $graphics = $canvas[1]
    Draw-Logo $graphics ([System.Drawing.RectangleF]::new(0, 0, 256, 256)) $false
    $hicon = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($hicon)
        $target = Join-Path $assets "AppIcon.ico"
        $stream = New-Object System.IO.FileStream($target, [System.IO.FileMode]::Create)
        try {
            $icon.Save($stream)
        }
        finally {
            $stream.Dispose()
            $icon.Dispose()
        }
    }
    finally {
        [NativeIconMethods]::DestroyIcon($hicon) | Out-Null
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Save-LogoPng "Square44x44Logo.scale-200.png" 88 88
Save-LogoPng "Square44x44Logo.targetsize-24_altform-unplated.png" 24 24
Save-LogoPng "Square44x44Logo.targetsize-48_altform-lightunplated.png" 48 48
Save-LogoPng "Square150x150Logo.scale-200.png" 300 300
Save-LogoPng "StoreLogo.png" 50 50
Save-LogoPng "LockScreenLogo.scale-200.png" 48 48
Save-LogoPng "Wide310x150Logo.scale-200.png" 620 300 $true
Save-LogoPng "SplashScreen.scale-200.png" 1240 600 $true
Save-AppIcon

Write-Host "Generated application logo assets in $assets"
