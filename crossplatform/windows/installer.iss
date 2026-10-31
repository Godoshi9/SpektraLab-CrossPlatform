#ifndef SourceDir
  #define SourceDir "..\..\build\app-windows"
#endif
[Setup]
AppId={{D9D873F4-83B7-443C-8944-573E582BD889}
AppName=SpektraLab
AppVersion=0.1.1
AppPublisher=Godoshi9
AppPublisherURL=https://github.com/Godoshi9/SpektraLab-CrossPlatform
DefaultDirName={localappdata}\Programs\SpektraLab
DefaultGroupName=SpektraLab
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=SpektraLab-0.1.1-Windows-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SpektraLab.exe
LicenseFile=..\..\license\LICENSE
CloseApplications=yes
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\SpektraLab"; Filename: "{app}\SpektraLab.exe"
Name: "{autodesktop}\SpektraLab"; Filename: "{app}\SpektraLab.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Run]
Filename: "{app}\SpektraLab.exe"; Description: "Launch SpektraLab"; Flags: nowait postinstall skipifsilent
