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
#ifdef PublicSetup
MinVersion=10.0.19045
#else
MinVersion=10.0
#endif
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
#ifdef PublicSetup
Source: "{#PublishDir}\*"; Excludes: "\libvlc\*,\Speech\FFmpeg\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{tmp}\build\x64\libvlc.*"; DestDir: "{app}\libvlc\win-x64"; Flags: external ignoreversion
Source: "{tmp}\build\x64\libvlccore.*"; DestDir: "{app}\libvlc\win-x64"; Flags: external ignoreversion
#define VlcTree(str Folder) 'Source: "{tmp}\build\x64\' + Folder + '\*"; DestDir: "{app}\libvlc\win-x64\' + Folder + '"; Flags: external ignoreversion recursesubdirs createallsubdirs'
{#VlcTree("hrtfs")}
{#VlcTree("lua")}
{#VlcTree("plugins")}
#define FFmpegRoot "ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1"
Source: "{tmp}\{#FFmpegRoot}\bin\*.dll"; DestDir: "{app}\Speech\FFmpeg"; Flags: external ignoreversion
Source: "{tmp}\{#FFmpegRoot}\bin\ffmpeg.exe"; DestDir: "{app}\Speech\FFmpeg"; Flags: external ignoreversion
Source: "{tmp}\{#FFmpegRoot}\bin\ffprobe.exe"; DestDir: "{app}\Speech\FFmpeg"; Flags: external ignoreversion
Source: "{tmp}\{#FFmpegRoot}\LICENSE.txt"; DestDir: "{app}\Speech\FFmpeg"; Flags: external ignoreversion
#else
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"

[InstallDelete]
Type: files; Name: "{autoprograms}\Lumen Player.lnk"
Type: files; Name: "{autoprograms}\Fotograma.lnk"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

#ifdef PublicSetup
#include "MediaPackages.iss"
#endif
