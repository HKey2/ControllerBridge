param([switch]$Restore, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build-app-icon.ps1')
# Build the normal image and embed that exact image with its checksum and version.
& (Join-Path $PSScriptRoot 'build-diagnostic.ps1') -Target ControllerBridge
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$cache = Get-Content -LiteralPath (Join-Path $taskRoot 'build/CMakeCache.txt') -Raw
if ($cache -notmatch '(?m)^PICO_BOARD:STRING=waveshare_rp2350_usb_a_diag\r?$') {
    throw 'The bundled updater requires the Waveshare RP2350-USB-A firmware build.'
}
$firmware = Join-Path $taskRoot 'build/ControllerBridge.uf2'
$version = (Get-Content -LiteralPath (Join-Path $taskRoot 'firmware-version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid bundled firmware version' }
$package = @{ product='ControllerBridge'; board='RP2350-USB-A'; version=$version; sha256=(Get-FileHash -LiteralPath $firmware -Algorithm SHA256).Hash }
$package | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'build/firmware-package.json') -Encoding utf8
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.cache\dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^9\.') { throw 'This new project requires the existing .NET 9 SDK. No toolchain is installed or changed by this script.' }
$project = Join-Path $taskRoot 'gui\ControllerBridge\ControllerBridge.csproj'
if ($Restore) {
    & dotnet restore $project --disable-parallel
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $taskRoot 'dist'
}
& dotnet publish $project -c Release --no-restore -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath $firmware -Destination (Join-Path $OutputDirectory 'ControllerBridge.uf2') -Force
exit $LASTEXITCODE
