Add-Type -AssemblyName System.Drawing

$output = Join-Path $PSScriptRoot '..\src\Shibori\Assets\shibori.ico'
$bitmap = [System.Drawing.Bitmap]::new(32, 32, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 77, 97, 215))
$graphics.FillRectangle($background, 1, 1, 30, 30)
$white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 2.5)
$graphics.DrawLine($white, 8, 11, 24, 11)
$graphics.DrawLine($white, 8, 16, 24, 16)
$graphics.DrawLine($white, 8, 21, 24, 21)

$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create($output)
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$white.Dispose()
$background.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
