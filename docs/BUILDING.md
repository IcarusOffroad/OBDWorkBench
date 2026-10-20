# Build, test and publish

## Windows requirements

Windows 10/11, .NET 8 SDK, a writable source folder, and internet access for the initial NuGet/runtime restore. USB serial drivers must be installed separately for hardware tests. Installing/configuring the optional GPS service requires administrator approval; building and normal Workbench use do not.

```powershell
dotnet restore OBDWorkbench.sln
dotnet build OBDWorkbench.sln -c Release --no-restore
dotnet run --project RegressionTests/RegressionTests.csproj -c Release
```

`build.bat` and `test-core.bat` provide the same build/test entry points. Workbench output: `OBDWorkbench/bin/Release/net8.0-windows/OBDWorkbench.exe`. The solution also builds the portable Core and Windows platform projects; the optional GPS service is separate.

## Self-contained Windows x64

Run `publish-win-x64.bat`, or:

```powershell
dotnet publish OBDWorkbench/OBDWorkbench.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `OBDWorkbench/bin/Release/net8.0-windows/win-x64/publish/`. Keep the full folder: executable, `Data`, `Docs`, `ReferenceData`, license/notices and communications reference. Single-file publishing does not place all external CSV/Markdown content inside the executable.

## Optional GPS service/monitor

From `GPSService/IcarusGPS_v0.2.4`, run `BUILD.bat`, then `INSTALL.bat`. Its build tests the parser/widget and publishes Service and Tray separately. See the [installation guide](../GPSService/README_WORKBENCH.md). A normal Workbench build does not install or start a Windows service. If the existing compatible service already runs, select it in Workbench without reinstalling.

## Checks and CI

Run `public-release-preflight.bat` on Windows, plus `python scripts/check-release.py` if Python 3 is available. The GitHub Windows workflow builds Workbench, runs the full Core suite, builds the GPS applications and runs the service parser/widget tests. The workflow configuration is supplied; it has not been run on GitHub by this package preparation.

Linux can compile Core and cross-compile Windows targets with `-p:EnableWindowsTargeting=true`, but it cannot run the WPF/WinForms UI or validate Windows service ACLs. In environments that prohibit local sockets, run Core tests with `-- --no-ipc`; this explicitly skips five GPS pipe checks and one TCP loopback check. A skipped check is not a passing check, and Windows release validation must run without this option.

No binaries, NuGet cache, local user settings, generated temporary projects or live captures belong in a source release.

## Build launcher diagnostics

Run build.bat from a fully extracted folder (not from inside the ZIP). It invokes scripts/Build-Workbench.ps1 through the built-in Windows PowerShell and saves the complete restore/build transcript to build.log beside the solution. It checks that dotnet.exe, an SDK and all four source projects are present, then targets OBDWorkbench.sln explicitly. The window remains open on success or failure. Reopen the build window after installing an SDK so PATH changes are picked up.

If Windows will not launch the batch file at all, open Command Prompt in the extracted folder and type build.bat. If the helper runs and fails, provide build.log. For a direct diagnostic bypass use the dotnet restore/build commands above. A PowerShell policy enforced by your organization may override the launcher's process-level policy setting.
