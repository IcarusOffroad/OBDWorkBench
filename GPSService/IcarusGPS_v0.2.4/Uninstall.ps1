$ErrorActionPreference='Stop'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $PSCommandPath)
 return
}
$destination=Join-Path $env:ProgramFiles 'Icarus\GPS'
$service=Get-Service IcarusGPS -ErrorAction SilentlyContinue
if($service){Stop-Service IcarusGPS -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20));& sc.exe delete IcarusGPS;if($LASTEXITCODE -ne 0){throw 'Could not remove service.'}}
Get-Process -Name IcarusGPS.Tray -ErrorAction SilentlyContinue | ForEach-Object {if($_.Path -eq (Join-Path $destination 'Tray\IcarusGPS.Tray.exe')){Stop-Process -Id $_.Id -Force}}
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name IcarusGPS -ErrorAction SilentlyContinue
if(Test-Path $destination){Remove-Item $destination -Recurse -Force}
Write-Host 'Service, startup entry, and installed programs removed. GPS settings and bounded diagnostic logs were retained in ProgramData\Icarus\GPS.'
