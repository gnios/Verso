#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "Verso-setup"
#endif

[Setup]
AppId={{8E6B2C41-7A1F-4D3E-9C50-A1B2C3D4E5F6}
AppName=Verso
AppVersion={#MyAppVersion}
AppPublisher=Verso
DefaultDirName={localappdata}\Programs\Verso
DefaultGroupName=Verso
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Verso.App.exe
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\..\src\Verso.App\Assets\verso.ico

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "data\*"

[Icons]
Name: "{group}\Verso"; Filename: "{app}\Verso.App.exe"
Name: "{autodesktop}\Verso"; Filename: "{app}\Verso.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Verso.App.exe"; Description: "Abrir o Verso"; Flags: nowait postinstall skipifsilent
