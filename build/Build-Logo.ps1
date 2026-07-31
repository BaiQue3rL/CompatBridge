[CmdletBinding()]
param(
    [switch]$Promote
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not ('CompatBridgeLogoRaster' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class CompatBridgeLogoRaster
{
    public static void MirrorVertically(Bitmap bitmap)
    {
        Rectangle rectangle = new Rectangle(
            0,
            0,
            bitmap.Width,
            bitmap.Height);
        BitmapData data = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (int y = 0; y < bitmap.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bitmap.Width / 2; x++)
                {
                    int source = row + (x * 4);
                    int target = row + ((bitmap.Width - 1 - x) * 4);
                    pixels[target] = pixels[source];
                    pixels[target + 1] = pixels[source + 1];
                    pixels[target + 2] = pixels[source + 2];
                    pixels[target + 3] = pixels[source + 3];
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
'@
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repositoryRoot 'assets'
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $artifactRoot)
}

function New-CompatBridgeLogoBitmap {
    param([Parameter(Mandatory = $true)][int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $bridge = New-Object System.Drawing.Drawing2D.GraphicsPath
    $loop = New-Object System.Drawing.Drawing2D.GraphicsPath
    $blue = New-Object System.Drawing.SolidBrush(
        [System.Drawing.ColorTranslator]::FromHtml('#0067B8')
    )
    $cyan = New-Object System.Drawing.SolidBrush(
        [System.Drawing.ColorTranslator]::FromHtml('#00A4C7')
    )

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode =
            [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode =
            [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale = [single]($Size / 1024.0)
        $graphics.ScaleTransform($scale, $scale)

        $bridge.StartFigure()
        $bridge.AddBezier(128, 400, 178, 230, 330, 124, 512, 124)
        $bridge.AddBezier(512, 124, 694, 124, 846, 230, 896, 400)
        $bridge.AddLine(896, 400, 947, 400)
        $bridge.AddLine(947, 400, 947, 612)
        $bridge.AddLine(947, 612, 859, 612)
        $bridge.AddLine(859, 612, 859, 504)
        $bridge.AddLine(859, 504, 704, 504)
        $bridge.AddLine(704, 504, 704, 454)
        $bridge.AddBezier(704, 454, 704, 359, 618, 282, 512, 282)
        $bridge.AddBezier(512, 282, 406, 282, 320, 359, 320, 454)
        $bridge.AddLine(320, 454, 320, 504)
        $bridge.AddLine(320, 504, 165, 504)
        $bridge.AddLine(165, 504, 165, 612)
        $bridge.AddLine(165, 612, 77, 612)
        $bridge.AddLine(77, 612, 77, 400)
        $bridge.CloseFigure()

        $loop.StartFigure()
        $loop.AddLine(184, 544, 320, 544)
        $loop.AddArc(
            (New-Object System.Drawing.RectangleF(320, 352, 384, 384)),
            180,
            -180
        )
        $loop.AddLine(704, 544, 840, 544)
        $loop.AddArc(
            (New-Object System.Drawing.RectangleF(184, 216, 656, 656)),
            0,
            180
        )
        $loop.CloseFigure()

        $graphics.FillPath($blue, $bridge)
        $graphics.FillPath($cyan, $loop)
    }
    finally {
        $cyan.Dispose()
        $blue.Dispose()
        $loop.Dispose()
        $bridge.Dispose()
        $graphics.Dispose()
    }
    if (($Size % 2) -eq 0) {
        [CompatBridgeLogoRaster]::MirrorVertically($bitmap)
    }
    return $bitmap
}

function Save-CompatBridgeIcon {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int[]]$Sizes
    )

    $images = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $Sizes) {
        $bitmap = New-CompatBridgeLogoBitmap -Size $size
        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save(
                $stream,
                [System.Drawing.Imaging.ImageFormat]::Png
            )
            $images.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $temporaryPath = $Path + '.tmp'
    $file = New-Object System.IO.FileStream(
        $temporaryPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    $writer = New-Object System.IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)
        $offset = 6 + (16 * $Sizes.Count)
        for ($index = 0; $index -lt $Sizes.Count; $index++) {
            $size = $Sizes[$index]
            $data = $images[$index]
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$data.Length)
            $writer.Write([uint32]$offset)
            $offset += $data.Length
        }
        foreach ($data in $images) {
            $writer.Write($data)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Save-SizePreview {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int[]]$Sizes
    )

    $canvas = New-Object System.Drawing.Bitmap(2048, 540)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $titleFont = New-Object System.Drawing.Font(
        'Microsoft YaHei UI',
        22,
        [System.Drawing.FontStyle]::Bold
    )
    $labelFont = New-Object System.Drawing.Font(
        'Segoe UI',
        14,
        [System.Drawing.FontStyle]::Regular
    )
    $textBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(45, 45, 45)
    )
    try {
        $graphics.Clear(
            [System.Drawing.ColorTranslator]::FromHtml('#F5F8FC')
        )
        $graphics.SmoothingMode =
            [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.InterpolationMode =
            [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawString(
            '严格对称版 · CompatBridge',
            $titleFont,
            $textBrush,
            36,
            24
        )

        $x = 36
        foreach ($size in $Sizes) {
            $displaySize = if ($size -eq 1024) { 384 } else { $size }
            $bitmap = New-CompatBridgeLogoBitmap -Size $size
            try {
                $y = 100 + [int]((384 - $displaySize) / 2)
                $graphics.DrawImage(
                    $bitmap,
                    (New-Object System.Drawing.Rectangle(
                        $x,
                        $y,
                        $displaySize,
                        $displaySize
                    ))
                )
                $label = if ($size -eq 1024) { '主标志' } else { "$size px" }
                $labelSize = $graphics.MeasureString($label, $labelFont)
                $graphics.DrawString(
                    $label,
                    $labelFont,
                    $textBrush,
                    [single]($x + (($displaySize - $labelSize.Width) / 2)),
                    492
                )
                $x += $displaySize + 54
            }
            finally {
                $bitmap.Dispose()
            }
        }
        $canvas.Save(
            $Path,
            [System.Drawing.Imaging.ImageFormat]::Png
        )
    }
    finally {
        $textBrush.Dispose()
        $labelFont.Dispose()
        $titleFont.Dispose()
        $graphics.Dispose()
        $canvas.Dispose()
    }
}

function Save-Comparison {
    param(
        [Parameter(Mandatory = $true)][string]$OldPath,
        [Parameter(Mandatory = $true)][string]$NewPath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $canvas = New-Object System.Drawing.Bitmap(1600, 880)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $oldLogo = [System.Drawing.Image]::FromFile($OldPath)
    $newLogo = [System.Drawing.Image]::FromFile($NewPath)
    $titleFont = New-Object System.Drawing.Font(
        'Microsoft YaHei UI',
        24,
        [System.Drawing.FontStyle]::Bold
    )
    $labelFont = New-Object System.Drawing.Font(
        'Microsoft YaHei UI',
        17,
        [System.Drawing.FontStyle]::Regular
    )
    $textBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(40, 40, 40)
    )
    $linePen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb(220, 225, 232),
        2
    )
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.InterpolationMode =
            [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawString(
            'CompatBridge Logo 对比',
            $titleFont,
            $textBrush,
            48,
            30
        )
        $graphics.DrawLine($linePen, 800, 105, 800, 825)
        $graphics.DrawString(
            '旧版：用错位缺口暗示 C',
            $labelFont,
            $textBrush,
            205,
            112
        )
        $graphics.DrawString(
            '新版：左右严格镜像',
            $labelFont,
            $textBrush,
            1000,
            112
        )
        $graphics.DrawImage(
            $oldLogo,
            (New-Object System.Drawing.Rectangle(80, 175, 640, 640))
        )
        $graphics.DrawImage(
            $newLogo,
            (New-Object System.Drawing.Rectangle(880, 175, 640, 640))
        )
        $canvas.Save(
            $Path,
            [System.Drawing.Imaging.ImageFormat]::Png
        )
    }
    finally {
        $linePen.Dispose()
        $textBrush.Dispose()
        $labelFont.Dispose()
        $titleFont.Dispose()
        $newLogo.Dispose()
        $oldLogo.Dispose()
        $graphics.Dispose()
        $canvas.Dispose()
    }
}

$stem = if ($Promote) {
    'compatbridge-logo-final'
}
else {
    'compatbridge-logo-symmetric'
}
$iconName = if ($Promote) {
    'CompatBridge.ico'
}
else {
    'CompatBridge-symmetric.ico'
}
$previewName = if ($Promote) {
    'compatbridge-icon-sizes.png'
}
else {
    'compatbridge-icon-sizes-symmetric.png'
}

$logoPath = Join-Path $assetRoot ($stem + '.png')
$iconPath = Join-Path $assetRoot $iconName
$previewPath = Join-Path $assetRoot $previewName
$oldLogoPath = Join-Path $assetRoot 'compatbridge-logo-final.png'

$logo = New-CompatBridgeLogoBitmap -Size 1024
try {
    $logo.Save($logoPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $logo.Dispose()
}

$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
Save-CompatBridgeIcon -Path $iconPath -Sizes $iconSizes
Save-SizePreview -Path $previewPath -Sizes @(1024, 256, 128, 64, 32, 16)

if (-not $Promote -and
    (Test-Path -LiteralPath $oldLogoPath -PathType Leaf)) {
    Save-Comparison `
        -OldPath $oldLogoPath `
        -NewPath $logoPath `
        -Path (Join-Path $artifactRoot 'CompatBridge-logo-comparison.png')
}

Write-Host "Logo:    $logoPath"
Write-Host "Icon:    $iconPath"
Write-Host "Preview: $previewPath"
