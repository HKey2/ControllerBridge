param([string]$OutputDirectory, [switch]$Restore)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.cache/dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if ((& dotnet --version) -notmatch '^9\.') { throw 'Use the existing .NET 9 SDK. This script does not install or change the toolchain.' }
$taskProduct = Join-Path $taskRoot 'gui/ControllerBridge/ControllerBridge.csproj'
if ($Restore) {
    & dotnet restore $taskProduct --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw 'Product restore failed.' }
}
& dotnet build $taskProduct -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Product build failed.' }
& dotnet build (Join-Path $taskRoot 'gui/DpiChecks/DpiChecks.csproj') -c Release --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'DPI check build failed.' }
$taskAssembly = Join-Path $taskRoot 'gui/ControllerBridge/bin/Release/net9.0-windows/win-x64/ControllerBridge.dll'
$taskProbe = Join-Path $taskRoot 'gui/DpiChecks/bin/Release/net9.0-windows/DpiChecks.exe'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'build/dpi-checks' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskResults = @()
foreach ($taskLanguage in @('ja', 'en')) {
    foreach ($taskDpi in @(96, 120, 144, 192)) {
        $taskCase = Join-Path $OutputDirectory ($taskLanguage + '-' + $taskDpi)
        if (Test-Path -LiteralPath $taskCase) { throw "Use a fresh output directory: $taskCase already exists." }
        $taskArguments = @(('"' + $taskAssembly + '"'), $taskDpi, ('"' + $taskCase + '"'), $taskLanguage)
        if ($taskDpi -eq 96) { $taskArguments += '--transitions' }
        $taskProcess = Start-Process -FilePath $taskProbe -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput ($taskCase + '.stdout.txt') -RedirectStandardError ($taskCase + '.stderr.txt')
        if (-not $taskProcess.WaitForExit(60000)) { throw "DPI check timed out (PID $($taskProcess.Id)): $taskCase" }
        if ($taskProcess.ExitCode -ne 0) { throw "DPI check failed: $taskCase. See its error.txt and stderr.txt." }
        $taskResult = Get-Content (Join-Path $taskCase 'dpi-checks.json') -Raw | ConvertFrom-Json
        if (-not $taskResult.passed) { throw "DPI check did not pass: $taskCase" }
        $taskResults += [pscustomobject]@{ language=$taskLanguage; initialDpi=$taskDpi; transitions=$taskResult.simulatedDpiMessages; checks=$taskResult.count }
        Get-Content ($taskCase + '.stdout.txt')
    }
}
[ordered]@{
    passed=$true; assemblySha256=(Get-FileHash -LiteralPath $taskAssembly).Hash; cases=$taskResults
    totalChecks=($taskResults | Measure-Object checks -Sum).Sum
    operatingSystemDisplayChanged=$false; physicalDeviceOpened=$false
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
