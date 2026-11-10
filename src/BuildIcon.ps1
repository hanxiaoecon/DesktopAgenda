param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'DesktopAgenda.ico' }
Add-Type -AssemblyName System.Drawing
$frames = @()
$sizes = @(16,24,32,48,64,128,256)
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $brushes = @{}
    foreach ($colorName in @('Navy','White','Black','Silver','Gray','ForestGreen','LightGray','Ivory')) { $brushes[$colorName] = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromName($colorName)) }
    $graphics.FillRectangle($brushes['Gray'], 28, 36, 205, 202)
    $graphics.FillRectangle($brushes['Black'], 13, 23, 205, 202)
    $graphics.FillRectangle($brushes['Ivory'], 19, 29, 193, 190)
    $graphics.FillRectangle($brushes['Navy'], 19, 29, 193, 52)
    $graphics.FillRectangle($brushes['White'], 27, 35, 177, 5)
    $graphics.FillRectangle($brushes['White'], 27, 89, 177, 5)
    foreach ($ringX in @(54,155)) {
        $graphics.FillRectangle($brushes['Black'], $ringX, 8, 23, 43)
        $graphics.FillRectangle($brushes['Silver'], ($ringX + 5), 12, 13, 33)
        $graphics.FillRectangle($brushes['White'], ($ringX + 5), 12, 5, 29)
    }
    foreach ($cellY in @(108,146,184)) { foreach ($cellX in @(38,78,118,158)) { $graphics.FillRectangle($brushes['Navy'], $cellX, $cellY, 22, 18) } }
    $graphics.FillRectangle($brushes['Black'], 122, 132, 122, 111)
    $graphics.FillRectangle($brushes['ForestGreen'], 127, 137, 112, 101)
    $graphics.FillRectangle($brushes['White'], 127, 137, 112, 5)
    $graphics.FillRectangle($brushes['White'], 127, 137, 5, 101)
    $tick = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 15)
    $tick.StartCap = [System.Drawing.Drawing2D.LineCap]::Square
    $tick.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $graphics.DrawLines($tick, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(147,187), [System.Drawing.PointF]::new(170,210), [System.Drawing.PointF]::new(220,159)))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save([System.IO.Path]::ChangeExtension($OutputPath, '.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $tick.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    foreach ($brush in $brushes.Values) { $brush.Dispose() }
}
$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($frameIndex = 0; $frameIndex -lt $sizes.Count; $frameIndex++) {
        $dimension = if ($sizes[$frameIndex] -eq 256) { 0 } else { $sizes[$frameIndex] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$frameIndex].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$frameIndex].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output $OutputPath
