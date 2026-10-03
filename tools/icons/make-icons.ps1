# Generates the application icons (AppIcon.ico/.png and the tray variant with an alert badge).
# Usage: pwsh tools/icons/make-icons.ps1 -OutDir src/HusqaCockpit.App/Assets
param([string]$OutDir)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size, [bool]$alert) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Background: rounded square, green gradient.
    $bg = New-RoundedRect (8 * $s) (8 * $s) (240 * $s) (240 * $s) (52 * $s)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 76, 175, 80)), ([System.Drawing.Color]::FromArgb(255, 27, 94, 32)), 90
    $g.FillPath($grad, $bg)

    # Lawn stripes at the bottom.
    $g.SetClip($bg)
    $stripe = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
    $g.FillRectangle($stripe, 0, 196 * $s, $size, 22 * $s)
    $g.ResetClip()

    # Mower body: white dome on a flat base.
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddArc(40 * $s, 70 * $s, 176 * $s, 200 * $s, 180, 180)
    $body.CloseFigure()
    $g.FillPath($white, $body)

    # Cockpit window (dark green) on the dome.
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 27, 94, 32))
    $win = New-Object System.Drawing.Drawing2D.GraphicsPath
    $win.AddArc(92 * $s, 102 * $s, 72 * $s, 64 * $s, 180, 180)
    $win.CloseFigure()
    $g.FillPath($dark, $win)

    # Wheels.
    $g.FillEllipse($dark, 58 * $s, 150 * $s, 46 * $s, 46 * $s)
    $g.FillEllipse($dark, 152 * $s, 150 * $s, 46 * $s, 46 * $s)
    $g.FillEllipse($white, 70 * $s, 162 * $s, 22 * $s, 22 * $s)
    $g.FillEllipse($white, 164 * $s, 162 * $s, 22 * $s, 22 * $s)

    if ($alert) {
        # Red badge in the top-right corner, with a white ring for contrast.
        $ring = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 211, 47, 47))
        $g.FillEllipse($ring, 140 * $s, 0, 116 * $s, 116 * $s)
        $g.FillEllipse($red, 152 * $s, 12 * $s, 92 * $s, 92 * $s)
    }

    $g.Dispose()
    return $bmp
}

function Write-Icon([string]$name, [bool]$alert) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $pngs = @{}
    foreach ($sz in $sizes) {
        $bmp = Draw-Icon $sz $alert
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs[$sz] = $ms.ToArray()
        $bmp.Dispose()
    }

    # ICO container with PNG-compressed entries.
    $fs = [System.IO.File]::Create((Join-Path $OutDir "$name.ico"))
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    foreach ($sz in $sizes) {
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$sz].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$sz].Length
    }
    foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }
    $bw.Dispose()

    [System.IO.File]::WriteAllBytes((Join-Path $OutDir "$name.png"), $pngs[256])
    "$name written to $OutDir"
}

Write-Icon 'AppIcon' $false
Write-Icon 'AppIconAlert' $true
