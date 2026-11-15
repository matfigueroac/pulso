#define AppName "Lumen Player"
#define AppExe "LumenPlayer.exe"
#define AppVersion GetStringFileInfo(SourcePath + "..\dist\LumenPlayer\" + AppExe, "ProductVersion")

[Setup]
AppId=LumenPlayer.Windows
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Lumen Player
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=LumenPlayer-{#AppVersion}-Setup-x64
SetupIconFile=..\src\Assets\Lumen.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "..\dist\LumenPlayer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent
