# Compatible with Windows PowerShell 5.1. No administrator rights needed.
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$transcriptStarted = $false
$buildExitCode = 1
try {
    Set-Location -LiteralPath $sourceRoot
    $logPath = Join-Path $sourceRoot 'build.log'
    Start-Transcript -Path $logPath -Force | Out-Null
    $transcriptStarted = $true
    Write-Host ('Source folder: ' + $sourceRoot)
    Write-Host ('Build log: ' + $logPath)
    $solution = Join-Path $sourceRoot 'OBDWorkbench.sln'
    if (-not (Test-Path -LiteralPath $solution)) {
        throw 'OBDWorkbench.sln is missing. Extract the complete source ZIP before building.'
    }
    foreach ($project in @('OBDWorkbench','OBDWorkbench.Core','OBDWorkbench.Platform.Windows','RegressionTests')) {
        $projectFile = Join-Path $sourceRoot ($project + '\' + $project + '.csproj')
        if (-not (Test-Path -LiteralPath $projectFile)) {
            throw ('Required source project is missing: ' + $projectFile)
        }
    }
    $dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dotnetCommand) {
        throw 'The .NET SDK was not found. Install the .NET 8 SDK (not only the Runtime), open a new build window, and retry: https://dotnet.microsoft.com/download/dotnet/8.0'
    }
    Write-Host ('Using: ' + $dotnetCommand.Source)
    $sdkList = @(& $dotnetCommand.Source --list-sdks)
    if ($LASTEXITCODE -ne 0 -or $sdkList.Count -eq 0) {
        throw 'No usable .NET SDK was found. Install the .NET 8 SDK, then reopen the build window.'
    }
    $sdkList | ForEach-Object { Write-Host $_ }
    Write-Host '[1/2] Restoring solution dependencies...'
    & $dotnetCommand.Source restore $solution
    if ($LASTEXITCODE -ne 0) { throw ('Restore failed with exit code ' + $LASTEXITCODE + '. See build.log above for the first error.') }
    Write-Host '[2/2] Building Release...'
    & $dotnetCommand.Source build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw ('Build failed with exit code ' + $LASTEXITCODE + '. See build.log above for the first error.') }
    $exePath = Join-Path $sourceRoot 'OBDWorkbench\bin\Release\net8.0-windows\OBDWorkbench.exe'
    if (-not (Test-Path -LiteralPath $exePath)) { throw ('Build returned success, but the expected executable was not found: ' + $exePath) }
    Write-Host ''
    Write-Host 'BUILD COMPLETE' -ForegroundColor Green
    Write-Host ('EXE: ' + $exePath)
    $buildExitCode = 0
} catch {
    Write-Host ''
    Write-Host 'BUILD FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'If this persists, send the build.log from the extracted source folder.'
} finally {
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
}
exit $buildExitCode
