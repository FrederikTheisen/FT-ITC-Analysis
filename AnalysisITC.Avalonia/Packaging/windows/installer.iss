#ifndef SourceDir
  #error SourceDir must be supplied by package-windows.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by package-windows.ps1
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by package-windows.ps1
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename must be supplied by package-windows.ps1
#endif
#ifndef AppPublisher
  #define AppPublisher "Frederik Theisen"
#endif

[Setup]
AppId={{A4F9B601-8F68-459E-9C27-96DDCAA595FB}
AppName=FT-ITC Analysis
AppVersion={#AppVersion}
AppVerName=FT-ITC Analysis {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://ft-itc.org
AppSupportURL=https://github.com/FrederikTheisen/FT-ITC-Analysis/issues
AppUpdatesURL=https://github.com/FrederikTheisen/FT-ITC-Analysis/releases
DefaultDirName={localappdata}\Programs\FT-ITC Analysis
DefaultGroupName=FT-ITC Analysis
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile={#SourcePath}\..\..\..\Resources\appicon.ico
UninstallDisplayIcon={app}\FT-ITC Analysis.exe
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
ChangesAssociations=yes
#ifdef SignedBuild
SignTool=ftitc
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FT-ITC Analysis"; Filename: "{app}\FT-ITC Analysis.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\FT-ITC Analysis"; Filename: "{app}\FT-ITC Analysis.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\FTITCAnalysis.Project"; ValueType: string; ValueName: ""; ValueData: "FT-ITC Analysis Project"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FTITCAnalysis.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\FT-ITC Analysis.exe,0"
Root: HKCU; Subkey: "Software\Classes\FTITCAnalysis.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FT-ITC Analysis.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.ftxtc\OpenWithProgids"; ValueType: string; ValueName: "FTITCAnalysis.Project"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\FT-ITC Analysis\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "FT-ITC Analysis"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\FT-ITC Analysis\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Process, analyze, and present isothermal titration calorimetry data."
Root: HKCU; Subkey: "Software\FT-ITC Analysis\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ftxtc"; ValueData: "FTITCAnalysis.Project"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "FT-ITC Analysis"; ValueData: "Software\FT-ITC Analysis\Capabilities"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\FT-ITC Analysis.exe"; Description: "Launch FT-ITC Analysis"; Flags: nowait postinstall skipifsilent
