param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
Start-Transcript -Path (Join-Path $PSScriptRoot 'build.log') -Force
try {
 if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 then run BUILD.bat again.' }
 & dotnet run --project Tests/Tests.csproj -c Release
 if($LASTEXITCODE -ne 0) { throw 'GPS parser tests failed.' }
 foreach($project in @('Service','Tray')) {
  & dotnet publish "$project/$project.csproj" -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "publish/$project"
  if($LASTEXITCODE -ne 0) { throw "Publish failed for $project." }
 }
 Write-Host 'Both applications are ready in publish. Run INSTALL.bat.' -ForegroundColor Green
} finally { Stop-Transcript }
