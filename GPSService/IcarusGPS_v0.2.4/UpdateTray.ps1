# Update a working installation without stopping or replacing the GPS service.
$ErrorActionPreference='Stop'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $PSCommandPath)
 return
}
$source=Join-Path $PSScriptRoot 'publish\Tray'
$destination=Join-Path $env:ProgramFiles 'Icarus\GPS\Tray'
if(-not(Test-Path (Join-Path $source 'IcarusGPS.Tray.exe'))){throw 'Run BUILD.bat successfully first.'}
if(-not(Test-Path (Join-Path $destination 'IcarusGPS.Tray.exe'))){throw 'No existing installation found. Run INSTALL.bat for a first installation.'}
Get-Process -Name IcarusGPS.Tray -ErrorAction SilentlyContinue | ForEach-Object {
 if($_.Path -eq (Join-Path $destination 'IcarusGPS.Tray.exe')){Stop-Process -Id $_.Id -Force}
}
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
Write-Host 'Tray and widget updated. The GPS service was left running and its settings were preserved.' -ForegroundColor Green
Write-Host 'Run OPEN_MONITOR.bat from the extracted folder now.'
Write-Host 'Then right-click the GPS tray icon and choose Recover widget / bring forward for 10 seconds.'
