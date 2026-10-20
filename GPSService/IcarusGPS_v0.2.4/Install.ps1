# Run through INSTALL.bat, which requests elevation.
$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $PSCommandPath)
 return
}
$destination=Join-Path $env:ProgramFiles 'Icarus\GPS'
$data=Join-Path $env:ProgramData 'Icarus\GPS'
foreach($part in @('Service','Tray')) {
 if(-not(Test-Path (Join-Path $PSScriptRoot "publish\$part\IcarusGPS.$part.exe"))) {throw 'Run BUILD.bat successfully before installing.'}
}
$existing=Get-Service -Name IcarusGPS -ErrorAction SilentlyContinue
if($existing){Stop-Service IcarusGPS -Force; $existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))}
# Stop only tray processes installed in our destination before updating their files.
Get-Process -Name IcarusGPS.Tray -ErrorAction SilentlyContinue | ForEach-Object {
 if($_.Path -eq (Join-Path $destination 'Tray\IcarusGPS.Tray.exe')){Stop-Process -Id $_.Id -Force}
}
New-Item -ItemType Directory -Path $destination,$data -Force | Out-Null
# Keep binaries and machine settings writable only by administrators and the service.
& icacls.exe $destination /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' '*S-1-5-19:(OI)(CI)RX'
if($LASTEXITCODE -ne 0){throw 'Could not set program permissions.'}
& icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M'
if($LASTEXITCODE -ne 0){throw 'Could not set data permissions.'}
foreach($part in @('Service','Tray')) {
 New-Item -ItemType Directory -Path (Join-Path $destination $part) -Force | Out-Null
 Copy-Item -Path (Join-Path $PSScriptRoot "publish\$part\*") -Destination (Join-Path $destination $part) -Recurse -Force
}
# Preserve any previously saved configuration.
$settings=Join-Path $data 'settings.json'
if(-not(Test-Path $settings)) { '{"Enabled":true,"Port":"COM6","Baud":9600,"Dtr":false,"Rts":false}' | Set-Content -Path $settings -Encoding UTF8 }
$binary='"'+(Join-Path $destination 'Service\IcarusGPS.Service.exe')+'"'
if($existing) {
 $record=Get-CimInstance Win32_Service -Filter "Name='IcarusGPS'"
 $changed=Invoke-CimMethod -InputObject $record -MethodName Change -Arguments @{PathName=$binary;StartMode='Automatic';StartName='NT AUTHORITY\LocalService';StartPassword=''}
 if($changed.ReturnValue -ne 0){throw "Could not update GPS service: code $($changed.ReturnValue)"}
} else {
 New-Service -Name IcarusGPS -BinaryPathName $binary -StartupType Automatic -DisplayName 'Icarus GPS Service' | Out-Null
 # New-Service defaults to LocalSystem; set the intended limited account before first start.
 $record=Get-CimInstance Win32_Service -Filter "Name='IcarusGPS'"
 $changed=Invoke-CimMethod -InputObject $record -MethodName Change -Arguments @{StartName='NT AUTHORITY\LocalService';StartPassword=''}
 if($changed.ReturnValue -ne 0){throw "Could not set LocalService account: code $($changed.ReturnValue). Service has not been started."}
}
& sc.exe description IcarusGPS 'Shared NMEA GPS receiver connection for Icarus applications.'
& sc.exe failure IcarusGPS reset= 86400 actions= restart/5000/restart/15000/restart/30000
if($LASTEXITCODE -ne 0){throw 'Could not configure service recovery.'}
$run='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $run -Name IcarusGPS -Value ('"'+(Join-Path $destination 'Tray\IcarusGPS.Tray.exe')+'" --tray') -PropertyType String -Force | Out-Null
Start-Service IcarusGPS
(Get-Service IcarusGPS).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
Write-Host 'Installed. The service starts at boot; the tray monitor and desktop widget start at sign-in.' -ForegroundColor Green
Write-Host 'Run OPEN_MONITOR.bat now to start the updated tray and desktop widget.'
Write-Host 'Existing direct-COM programs must be closed or GPS disabled here before they can use that port.'
