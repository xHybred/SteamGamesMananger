# Generates the Steam Games Manager app icon (.ico) from the source logo PNG.
#
# Input : docs/images/sgm-logo-source.png   (the author's logo)
# Output: Sgm.App/Resources/sgm.ico          (multi-resolution PNG icon)
#         docs/images/sgm-logo.png           (1024px master, for reference)
#
# The source is cropped to the opaque bounding box and squared so the badge fills the
# icon canvas (no transparent padding, which would make the icon look smaller than its
# neighbours in the taskbar). Each target size is produced by a stepped (repeated
# half-size) high-quality downscale, which keeps the thin cut-outs crisp.

Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent $PSScriptRoot
$src    = Join-Path $root 'docs\images\sgm-logo-source.png'
$icoOut = Join-Path $root 'Sgm.App\Resources\sgm.ico'
$pngOut = Join-Path $root 'docs\images\sgm-logo.png'

if (-not (Test-Path $src)) { throw "Source logo not found: $src" }

# --- Load the source and find the opaque bounding box.
# A small alpha threshold (16) is used so faint edge noise/shadow does not inflate the box. ---
$orig = New-Object System.Drawing.Bitmap $src
$w = $orig.Width; $h = $orig.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$data = $orig.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$orig.UnlockBits($data)

$minX = $w; $minY = $h; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $w; $x++) {
        if ($bytes[$row + ($x * 4) + 3] -gt 16) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
if ($maxX -lt 0) { throw "Source logo has no opaque pixels: $src" }

$cw = $maxX - $minX + 1
$ch = $maxY - $minY + 1
Write-Output ("source {0}x{1}  opaque box {2}x{3} at ({4},{5})" -f $w, $h, $cw, $ch, $minX, $minY)

# --- Crop to the box and centre it on a square transparent canvas ---
$crop = New-Object System.Drawing.Bitmap $cw, $ch, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gc = [System.Drawing.Graphics]::FromImage($crop)
$gc.DrawImage($orig, (New-Object System.Drawing.Rectangle 0, 0, $cw, $ch), (New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch), [System.Drawing.GraphicsUnit]::Pixel)
$gc.Dispose()
$orig.Dispose()

$side = [Math]::Max($cw, $ch)
$square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gs = [System.Drawing.Graphics]::FromImage($square)
$gs.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$gs.Clear([System.Drawing.Color]::Transparent)
$gs.DrawImage($crop, [int](($side - $cw) / 2), [int](($side - $ch) / 2), $cw, $ch)
$gs.Dispose()
$crop.Dispose()

# --- Stepped high-quality downscale to an exact square size ---
function Resize-HighQuality {
    param([System.Drawing.Bitmap]$Source, [int]$Size)
    $cur = $Source
    $owned = $false
    while ($cur.Width -gt $Size * 2) {
        $nw = [int][Math]::Ceiling($cur.Width / 2.0)
        $nh = [int][Math]::Ceiling($cur.Height / 2.0)
        $n = New-Object System.Drawing.Bitmap $nw, $nh, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $ng = [System.Drawing.Graphics]::FromImage($n)
        $ng.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $ng.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $ng.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $ng.DrawImage($cur, (New-Object System.Drawing.Rectangle 0, 0, $nw, $nh))
        $ng.Dispose()
        if ($owned) { $cur.Dispose() }
        $cur = $n
        $owned = $true
    }
    $out = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $og = [System.Drawing.Graphics]::FromImage($out)
    $og.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $og.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $og.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $og.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $og.DrawImage($cur, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size))
    $og.Dispose()
    if ($owned) { $cur.Dispose() }
    return $out
}

# --- Build every frame (Microsoft's recommended size ladder) ---
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = Resize-HighQuality -Source $square -Size $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $s; Data = $ms.ToArray() }
    $bmp.Dispose()
    $ms.Dispose()
}

# --- Pack the PNG-icon container ---
$fs = [System.IO.File]::Create($icoOut)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$frames.Count)     # image count

$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)               # colour count
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bit depth
    $bw.Write([UInt32]$f.Data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write([Byte[]]$f.Data) }
$bw.Flush()
$bw.Close()
$fs.Close()

# --- 1024px PNG master ---
$master = Resize-HighQuality -Source $square -Size 1024
$master.Save($pngOut, [System.Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()
$square.Dispose()

Write-Output ("Wrote {0} ({1} frames: {2})" -f $icoOut, $frames.Count, ($sizes -join ', '))
Write-Output ("Wrote {0}" -f $pngOut)
