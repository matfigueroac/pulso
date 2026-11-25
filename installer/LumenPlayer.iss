#define AppName "Pulso"
#define AppExe "LumenPlayer.exe"
#ifndef PublishDir
  #define PublishDir SourcePath + "..\dist\LumenPlayer"
#endif
#define AppVersion GetStringFileInfo(PublishDir + "\" + AppExe, "ProductVersion")

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
OutputBaseFilename={#AppName}-{#AppVersion}-Setup-x64
SetupIconFile=..\src\Assets\Lumen.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"

[InstallDelete]
Type: files; Name: "{autoprograms}\Lumen Player.lnk"
Type: files; Name: "{autoprograms}\Fotograma.lnk"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent
