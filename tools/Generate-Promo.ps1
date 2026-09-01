param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$images = Join-Path $ProjectRoot 'docs\images'
$backgroundPath = Join-Path $images 'hero-background.png'
$logoPath = Join-Path $ProjectRoot 'Assets\UsSDAU-logo-square-v2.png'

function New-Canvas {
    $bitmap = [System.Drawing.Bitmap]::new(1920, 1080, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $background = [System.Drawing.Image]::FromFile($backgroundPath)
    $graphics.DrawImage($background, 0, 0, 1920, 1080)
    $background.Dispose()
    return @($bitmap, $graphics)
}

function New-RoundedPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $diameter = $radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-PanelImage($graphics, [string]$path, [System.Drawing.RectangleF]$rect, [float]$radius = 24) {
    $shadowRect = [System.Drawing.RectangleF]::new($rect.X + 14, $rect.Y + 20, $rect.Width, $rect.Height)
    $shadowPath = New-RoundedPath $shadowRect $radius
    $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(82, 0, 12, 34))
    $graphics.FillPath($shadowBrush, $shadowPath)
    $shadowBrush.Dispose(); $shadowPath.Dispose()

    $clipPath = New-RoundedPath $rect $radius
    $state = $graphics.Save()
    $graphics.SetClip($clipPath)
    $image = [System.Drawing.Image]::FromFile($path)
    $graphics.DrawImage($image, $rect)
    $image.Dispose()
    $graphics.Restore($state)
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(120, 255, 255, 255), 2)
    $graphics.DrawPath($outline, $clipPath)
    $outline.Dispose(); $clipPath.Dispose()
}

function Draw-Text($graphics, [string]$text, [float]$size, [System.Drawing.FontStyle]$style, [System.Drawing.Color]$color, [float]$x, [float]$y, [string]$family = 'Microsoft YaHei UI') {
    $font = [System.Drawing.Font]::new($family, $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new($color)
    $graphics.DrawString($text, $font, $brush, $x, $y)
    $brush.Dispose(); $font.Dispose()
}

# Hero banner.
$canvas = New-Canvas
$hero = $canvas[0]; $g = $canvas[1]
$shade = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(112, 3, 16, 40))
$g.FillRectangle($shade, 0, 0, 930, 1080); $shade.Dispose()
$logo = [System.Drawing.Image]::FromFile($logoPath)
$g.DrawImage($logo, 106, 96, 112, 112); $logo.Dispose()
Draw-Text $g 'UsSDAU' 66 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::White) 104 222 'Segoe UI'
Draw-Text $g '把课程、成绩与每一天放在桌面上' 31 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(230, 231, 241, 255)) 108 310
Draw-Text $g '山农学生的 Windows 学业桌面' 20 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(190, 190, 217, 244)) 110 365
Draw-Text $g '每日 / 周课表' 20 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(255, 74, 218, 240)) 112 490
Draw-Text $g '多学期成绩与 PDF 成绩单' 20 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(255, 74, 218, 240)) 112 544
Draw-Text $g '桌面灵动岛与课前提醒' 20 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(255, 74, 218, 240)) 112 598
Draw-Text $g '.NET 10  ·  WPF  ·  Local-first' 17 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(170, 202, 222, 246)) 112 912 'Segoe UI'
Draw-PanelImage $g (Join-Path $images 'daily-schedule.png') ([System.Drawing.RectangleF]::new(744, 250, 1100, 709)) 28
Draw-PanelImage $g (Join-Path $images 'schedule-island.png') ([System.Drawing.RectangleF]::new(810, 105, 940, 94)) 30
$hero.Save((Join-Path $images 'hero.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $hero.Dispose()

# Academic records showcase.
$canvas = New-Canvas
$showcase = $canvas[0]; $g = $canvas[1]
$overlay = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(142, 4, 18, 44))
$g.FillRectangle($overlay, 0, 0, 1920, 1080); $overlay.Dispose()
Draw-Text $g '一份属于你的学业记录' 48 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::White) 104 74
Draw-Text $g '从课程概览到正式成绩单，多学期数据保持清晰、可导出、可离线校验。' 23 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(220, 220, 236, 255)) 108 146
Draw-PanelImage $g (Join-Path $images 'score-overview.png') ([System.Drawing.RectangleF]::new(84, 230, 930, 600)) 26
Draw-PanelImage $g (Join-Path $images 'transcript-preview.png') ([System.Drawing.RectangleF]::new(906, 322, 930, 600)) 26
Draw-Text $g '成绩概览' 18 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(255, 70, 218, 240)) 92 856
Draw-Text $g '自绘正式成绩单 · PDF · 页码 · 离线二维码' 18 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(255, 70, 218, 240)) 914 948
$showcase.Save((Join-Path $images 'academic-records.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $showcase.Dispose()

Write-Output "Generated: $images\hero.png"
Write-Output "Generated: $images\academic-records.png"
