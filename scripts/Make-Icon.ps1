$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Canonical production artwork for the approved classic donut.
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\TokenWatch\Assets'))
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
$filled = [Drawing.ColorTranslator]::FromHtml('#326DFA')
$remaining = [Drawing.ColorTranslator]::FromHtml('#CAD4E3')
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach($size in $sizes){
    $pixels = $size * 8
    $large = [Drawing.Bitmap]::new($pixels,$pixels,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $drawing = [Drawing.Graphics]::FromImage($large)
    $blueBrush = [Drawing.SolidBrush]::new($filled)
    $grayBrush = [Drawing.SolidBrush]::new($remaining)
    $clearBrush = [Drawing.SolidBrush]::new([Drawing.Color]::Transparent)
    $small = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $resample = [Drawing.Graphics]::FromImage($small)
    $buffer = [IO.MemoryStream]::new()
    try {
        $drawing.Clear([Drawing.Color]::Transparent)
        $drawing.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $outer = [Drawing.RectangleF]::new($pixels/16,$pixels/16,$pixels*7/8,$pixels*7/8)
        $drawing.FillEllipse($blueBrush,$outer)
        $drawing.FillPie($grayBrush,$outer,270,90)
        $drawing.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $inner = [Drawing.RectangleF]::new($pixels*7/32,$pixels*7/32,$pixels*9/16,$pixels*9/16)
        $drawing.FillEllipse($clearBrush,$inner)
        $resample.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $resample.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $resample.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $resample.DrawImage($large,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$pixels,$pixels,[Drawing.GraphicsUnit]::Pixel)
        $small.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
        $bytes=$buffer.ToArray()
        $frames.Add($bytes)
        if($size -eq 256){[IO.File]::WriteAllBytes((Join-Path $assetRoot 'tokenwatch.png'),$bytes)}
        if($size -in @(16,20,24,32)){
            $proofRoot=Join-Path $PSScriptRoot '..\artifacts\branding'
            New-Item -ItemType Directory -Path $proofRoot -Force | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $proofRoot ('tokenwatch-'+$size+'.png')),$bytes)
        }
    } finally {
        $buffer.Dispose();$resample.Dispose();$small.Dispose();$drawing.Dispose();$large.Dispose()
        $blueBrush.Dispose();$grayBrush.Dispose();$clearBrush.Dispose()
    }
}
$writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assetRoot 'TokenWatch.ico')))
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++){
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose()}
$svg=@'
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <title>TokenWatch</title>
  <path fill="#326DFA" d="M30 16A14 14 0 1 1 16 2L16 7A9 9 0 1 0 25 16Z"/>
  <path fill="#CAD4E3" d="M16 2A14 14 0 0 1 30 16L25 16A9 9 0 0 0 16 7Z"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'TokenWatch.svg'),$svg)
