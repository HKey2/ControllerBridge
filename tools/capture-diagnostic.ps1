param(
    [string]$Port='COM4',
    [int]$Seconds=15,
    [string]$Command='d',
    [string]$OutputPath,
    [int]$SwitchTo1msAfterSeconds=-1,
    [string]$StopFile
)
$ErrorActionPreference='Stop'
if (-not $OutputPath) { throw 'OutputPath is required.' }
$writer = New-Object System.IO.StreamWriter($OutputPath,$false,(New-Object System.Text.UTF8Encoding($false)))
$serialPort = New-Object System.IO.Ports.SerialPort($Port,115200,[System.IO.Ports.Parity]::None,8,[System.IO.Ports.StopBits]::One)
$serialPort.DtrEnable=$true
$serialPort.RtsEnable=$false
$serialPort.ReadTimeout=100
$serialPort.WriteTimeout=500
$switched=$false
try {
    $serialPort.Open()
    $timer=[System.Diagnostics.Stopwatch]::StartNew()
    if ($Command) { $serialPort.Write($Command) }
    while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
        if ($StopFile -and (Test-Path -LiteralPath $StopFile)) { break }
        $chunk=$serialPort.ReadExisting()
        if ($chunk) { $writer.Write($chunk); $writer.Flush() }
        if (-not $switched -and $SwitchTo1msAfterSeconds -ge 0 -and $timer.Elapsed.TotalSeconds -ge $SwitchTo1msAfterSeconds) {
            $writer.WriteLine('CAPTURE command=1 experimental_1ms')
            $serialPort.Write('1')
            $switched=$true
        }
        Start-Sleep -Milliseconds 5
    }
    $chunk=$serialPort.ReadExisting()
    if ($chunk) { $writer.Write($chunk) }
} finally {
    if ($serialPort.IsOpen) { $serialPort.Close() }
    $serialPort.Dispose()
    $writer.Dispose()
}
Get-Content -LiteralPath $OutputPath | Select-Object -Last 100
