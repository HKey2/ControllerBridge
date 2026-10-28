param([string]$BuildType='Release',[ValidateSet('egret_diagnostic','ControllerBridge','egret_poll_probe')][string]$Target='ControllerBridge',[switch]$UsbTrace,[switch]$MouseFixedProbe,[ValidateRange(1,32767)][int]$MouseProbeAmplitude=10000,[ValidateSet(8,16)][int]$MousePacketBytes=8,[string]$PicoSdkPath=$env:PICO_SDK_PATH,[string]$PicoToolsRoot)
$ErrorActionPreference='Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $PicoToolsRoot) { $PicoToolsRoot = Join-Path $env:USERPROFILE '.pico-sdk' }
if (-not $PicoSdkPath) { $PicoSdkPath = Join-Path $PicoToolsRoot 'sdk\2.1.1' }
$env:PICO_SDK_PATH = $PicoSdkPath
$toolRoot = $PicoToolsRoot
$lock = Get-Content -LiteralPath (Join-Path $taskRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
function Assert-PinnedSource([string]$Path,[string]$Expected,$AllowedChanges=@()) {
    $actual = & git -C $Path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $Expected) {
        throw "Dependency revision differs from dependencies.lock.json: $Path"
    }
    # Submodule revisions are checked by Git; their worktree changes are checked
    # separately below. Never reset or rewrite the installed SDK.
    $changed = @(& git -C $Path diff --name-only --ignore-submodules=dirty HEAD --)
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect dependency changes: $Path" }
    foreach ($changedPath in $changed) {
        $allowed = @($AllowedChanges | Where-Object { $_.path -eq $changedPath })
        if ($allowed.Count -ne 1) { throw "Unrecorded dependency change: $Path/$changedPath" }
        $hash = (Get-FileHash -LiteralPath (Join-Path $Path $changedPath) -Algorithm SHA256).Hash
        if ($hash -ne $allowed[0].sha256) { throw "Dependency content changed: $Path/$changedPath" }
    }
}
Assert-PinnedSource $env:PICO_SDK_PATH $lock.pico_sdk.commit
Assert-PinnedSource (Join-Path $env:PICO_SDK_PATH 'lib\tinyusb') $lock.tinyusb_diagnostic_cdc_only.commit $lock.tinyusb_diagnostic_cdc_only.allowed_existing_changes
$vendor = Join-Path $taskRoot 'third_party\Pico-PIO-USB'
$manifest = Get-Content -LiteralPath (Join-Path $taskRoot 'third_party\pico-pio-usb.sha256.json') -Raw | ConvertFrom-Json
if ($manifest.commit -ne $lock.pico_pio_usb.commit) { throw 'Vendored source revision differs from dependencies.lock.json' }
foreach ($entry in $manifest.files.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $vendor $entry.Name) -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Vendored dependency content changed: $($entry.Name)"
    }
}
$cmake = Join-Path $toolRoot 'cmake\v3.31.5\bin\cmake.exe'
$traceOption = if ($UsbTrace) { 'ON' } else { 'OFF' }
$probeOption = if ($MouseFixedProbe) { 'ON' } else { 'OFF' }
& $cmake -S $taskRoot -B (Join-Path $taskRoot 'build') -G Ninja "-DCMAKE_BUILD_TYPE=$BuildType" '-DPICO_COMPILER=pico_arm_cortex_m33_gcc' "-DPICO_TOOLCHAIN_PATH=$toolRoot/toolchain/14_2_Rel1" "-DCMAKE_MAKE_PROGRAM=$toolRoot/ninja/v1.12.1/ninja.exe" "-Dpioasm_DIR=$toolRoot/tools/2.1.1/pioasm" "-Dpicotool_DIR=$toolRoot/picotool/2.1.1/picotool"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $cmake -S $taskRoot -B (Join-Path $taskRoot 'build') "-DBRIDGE_USB_TRACE=$traceOption" "-DBRIDGE_MOUSE_FIXED_PROBE=$probeOption" "-DBRIDGE_MOUSE_FIXED_AMPLITUDE=$MouseProbeAmplitude" "-DBRIDGE_MOUSE_PACKET_BYTES=$MousePacketBytes"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $cmake --build (Join-Path $taskRoot 'build') --target $Target --parallel 4
exit $LASTEXITCODE
