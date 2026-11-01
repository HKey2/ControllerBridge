param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $taskRoot 'test/udlr'
$firmwareBuild = Join-Path $testRoot 'build'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $testRoot 'dist' }
& (Join-Path $PSScriptRoot 'build-diagnostic.ps1') -Target ControllerBridge -BuildDirectory $firmwareBuild -UdlrTest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$firmware = Join-Path $firmwareBuild 'ControllerBridge.uf2'
$version = (Get-Content -LiteralPath (Join-Path $taskRoot 'firmware-version.txt') -Raw).Trim() + '-udlr'
@{product='ControllerBridge';board='RP2350-USB-A';version=$version;sha256=(Get-FileHash -LiteralPath $firmware).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $firmwareBuild 'firmware-package.json') -Encoding utf8
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.cache/dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'; $env:DOTNET_NOLOGO='1'
if ((& dotnet --version) -notmatch '^9\.') { throw 'Use the existing .NET 9 SDK. This script does not install toolchains.' }
& dotnet publish (Join-Path $taskRoot 'gui/ControllerBridge/ControllerBridge.csproj') -c Release -o $OutputDirectory --ignore-failed-sources '-p:UdlrTest=true' "-p:FirmwareBuildDirectory=$firmwareBuild" "-p:MSBuildProjectExtensionsPath=$testRoot/obj/" "-p:IntermediateOutputPath=$testRoot/obj/Release/" "-p:OutputPath=$testRoot/bin/"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath $firmware -Destination (Join-Path $OutputDirectory 'ControllerBridgeUdlrTest.uf2') -Force
