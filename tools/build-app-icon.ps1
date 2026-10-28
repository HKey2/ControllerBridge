$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskInput = Join-Path $taskRoot 'Resources/ControllerBridge_icon.png'
$taskOutput = Join-Path $taskRoot 'Resources/ControllerBridge_icon.ico'
Add-Type -AssemblyName System.Drawing
$taskSource = [System.Drawing.Image]::FromFile($taskInput)
$taskSizes = @(16,20,24,32,40,48,64,128,256)
$taskFrames = [System.Collections.Generic.List[byte[]]]::new()
try {
    if ($taskSource.Width -ne $taskSource.Height) { throw 'Application icon source must be square' }
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = [System.Drawing.Bitmap]::new($taskSize,$taskSize,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
        $taskStream = [System.IO.MemoryStream]::new()
        $taskAttributes = [System.Drawing.Imaging.ImageAttributes]::new()
        try {
            $taskGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $taskGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            # Preserve the source's pixel art for exact-size and enlarged frames.
            $taskGraphics.InterpolationMode = if ($taskSize -ge $taskSource.Width) {
                [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            } else { [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic }
            $taskAttributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $taskGraphics.DrawImage($taskSource,[System.Drawing.Rectangle]::new(0,0,$taskSize,$taskSize),0,0,$taskSource.Width,$taskSource.Height,[System.Drawing.GraphicsUnit]::Pixel,$taskAttributes)
            $taskBitmap.Save($taskStream,[System.Drawing.Imaging.ImageFormat]::Png)
            $taskFrames.Add($taskStream.ToArray())
        } finally {
            $taskAttributes.Dispose(); $taskStream.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
        }
    }
} finally { $taskSource.Dispose() }

$taskWriter = [System.IO.BinaryWriter]::new([System.IO.File]::Create($taskOutput))
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
        $taskSide = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
        $taskWriter.Write([byte]$taskSide); $taskWriter.Write([byte]$taskSide)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskFrames[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskFrames[$taskIndex].Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write($taskFrame) }
} finally { $taskWriter.Dispose() }
Write-Output "Application icon generated: $($taskSizes -join ', ') px."
