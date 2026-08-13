# Windows 10: first local packaging trial

This guide builds an unsigned x64 installer on your own Windows 10 computer.
Run one block at a time. Read the result before continuing.

The first trial does not require a GitHub account, SSH key, Visual Studio,
Clang, a C++ toolchain, a signing certificate, or Microsoft Partner Center.

## 1. Check the computer

Press **Windows+R**, enter `winver`, and press Enter.

Use Windows 10 version 22H2, OS build 19045, on an x64 computer. Install all
available Windows updates first. Windows 10 Home and Pro are out of ordinary
support, so do not put a production code-signing certificate on an unpatched
machine.

Open **Windows PowerShell** and run:

```powershell
Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, OSArchitecture
winget --version
```

If `winget` is not found, update **App Installer** from Microsoft Store. You
can also try this registration command and then open a new terminal:

```powershell
Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe
```

## 2. Install the five build tools

Open **Windows PowerShell as Administrator**. Run these commands separately:

```powershell
winget source update
```

```powershell
winget install --exact --id Git.Git --source winget --accept-package-agreements --accept-source-agreements
```

```powershell
winget install --exact --id Microsoft.PowerShell --source winget --accept-package-agreements --accept-source-agreements
```

```powershell
winget install --exact --id Microsoft.DotNet.SDK.10 --source winget --accept-package-agreements --accept-source-agreements
```

```powershell
winget install --exact --id Microsoft.WindowsSDK.10.0.28000 --source winget --accept-package-agreements --accept-source-agreements
```

```powershell
winget install --exact --id JRSoftware.InnoSetup --source winget --accept-package-agreements --accept-source-agreements
```

Close the administrator window. From this point onward, use a normal,
non-administrator account.

## 3. Open PowerShell 7 and verify the tools

Open **PowerShell 7** from the Start menu. Run:

```powershell
$PSVersionTable.PSVersion
git --version
dotnet --version
```

PowerShell must be 7.4 or newer and `dotnet --version` must start with `10.`.

Find Inno Setup:

```powershell
$Inno = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
Test-Path $Inno
```

The result should be `True`. If it is `False`, run:

```powershell
Get-ChildItem "C:\Program Files*\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue
```

Set `$Inno` to the path printed by that command.

The Windows SDK is only needed for Store packaging and signing, but verify it
now:

```powershell
Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory | Sort-Object Name
```

At least one version directory should be listed.

## 4. Clone the public repository

No GitHub login or SSH key is required:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\source"
Set-Location "$env:USERPROFILE\source"
git clone https://github.com/FrederikTheisen/FT-ITC-Analysis.git
Set-Location .\FT-ITC-Analysis
git status --short
git log -1 --oneline
```

`git status --short` should print nothing. For this first trial, build the
current `master` branch. A real release will instead use a clean `v<version>`
tag.

If the checkout already exists, do not clone it again. Use:

```powershell
Set-Location "$env:USERPROFILE\source\FT-ITC-Analysis"
git status --short
git pull --ff-only
```

Do not pull if `git status --short` reports changes you want to keep.

## 5. Run the tests

Run the test projects separately:

```powershell
dotnet test .\AnalysisITC.Core.Tests\AnalysisITC.Core.Tests.csproj --configuration Release
```

```powershell
dotnet test .\AnalysisITC.Avalonia.Tests\AnalysisITC.Avalonia.Tests.csproj --configuration Release
```

For a release, stop if either command fails. During this first packaging trial,
record the failure and continue only if the purpose is to check the Windows
publication and installer mechanics.

## 6. Publish the Windows application

Run:

```powershell
$Root = (Get-Location).Path
$Project = Join-Path $Root "AnalysisITC.Avalonia\AnalysisITC.Avalonia.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
```

```powershell
dotnet restore $Project --runtime win-x64
```

```powershell
Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
```

```powershell
dotnet publish $Project --configuration Release --runtime win-x64 --self-contained true --no-restore --output $PublishDir
```

Verify and launch the unpackaged application:

```powershell
$Application = Join-Path $PublishDir "FT-ITC Analysis.exe"
Test-Path $Application
& $Application
```

`Test-Path` must print `True`. Open a project and perform a basic operation,
then close the application before continuing.

## 7. Compile the unsigned installer

Set the remaining paths and read the version from the project:

```powershell
[xml]$ProjectXml = Get-Content $Project
$Version = [string]$ProjectXml.Project.PropertyGroup.Version
$PackageDir = Join-Path $Root "artifacts\packages"
$InstallerDefinition = Join-Path $Root "AnalysisITC.Avalonia\Packaging\windows\installer.iss"
$InstallerBaseName = "FT-ITC-Analysis-$Version-win-x64-setup"
$Installer = Join-Path $PackageDir "$InstallerBaseName.exe"
New-Item -ItemType Directory -Force $PackageDir
```

Compile it:

```powershell
& $Inno /Qp "/DSourceDir=$PublishDir" "/DOutputDir=$PackageDir" "/DAppVersion=$Version" "/DOutputBaseFilename=$InstallerBaseName" "/DAppPublisher=Frederik Theisen" $InstallerDefinition
```

Verify the output:

```powershell
Test-Path $Installer
Get-Item $Installer | Select-Object FullName, Length, LastWriteTime
Get-FileHash $Installer -Algorithm SHA256
```

An unsigned installer will say **Unknown publisher** and may trigger
SmartScreen. That is expected for this trial.

## 8. Install and test it

Launch the installer as the normal user:

```powershell
& $Installer
```

Then check:

1. Installation does not request administrator elevation.
2. The Start-menu shortcut launches the application.
3. The application is installed below
   `%LOCALAPPDATA%\Programs\FT-ITC Analysis`.
4. Windows offers FT-ITC Analysis as an application for `.ftxtc` files.
5. Double-clicking an associated `.ftxtc` project opens it.
6. `.ftitc` is not associated with the application.
7. A legacy `.ftitc` project still opens from **File > Open**.
8. Settings and autosaves are created below `%APPDATA%\AnalysisITC\Avalonia`.

## 9. Test uninstall

Use **Settings > Apps > Apps & features > FT-ITC Analysis > Uninstall**.

Confirm that the installed application directory, shortcuts, and `.ftxtc`
registration disappear. Confirm that your projects and
`%APPDATA%\AnalysisITC\Avalonia` remain.

## 10. Save the result

The installer is located at:

```text
artifacts\packages\FT-ITC-Analysis-<version>-win-x64-setup.exe
```

For tomorrow's test, sending back the output of the first failed command is
more useful than continuing through cascading errors.

## Later: release and Store work

Do not tackle signing or the Store during the first local trial. After the
unsigned installer passes installation, upgrade, and uninstall testing:

- build from a clean immutable `v<version>` tag;
- configure a trusted code-signing certificate or signing service;
- reserve the Store product and obtain its exact identity and publisher;
- build the unsigned Partner Center MSIX using the commands in `README.md`;
- run the Windows App Certification Kit; and
- publish the installer and checksum deliberately.

## Compatibility references

- [Install .NET on Windows](https://learn.microsoft.com/dotnet/core/install/windows)
- [Use WinGet to install and manage applications](https://learn.microsoft.com/windows/package-manager/winget/)
- [Windows and SDK version overview](https://learn.microsoft.com/windows/apps/get-started/versioning-overview)
- [Windows 10 release information](https://learn.microsoft.com/windows/release-health/release-information)
