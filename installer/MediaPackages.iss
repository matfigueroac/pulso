[Code]
const
  VlcPackage = 'videolan.libvlc.windows.3.0.24.nupkg';
  FFmpegPackage = '{#FFmpegRoot}.zip';
var
  MediaDownloads: TDownloadWizardPage;

procedure InitializeWizard;
begin
  MediaDownloads := CreateDownloadPage('Preparando los motores de video y audio',
    'Pulso comprueba la integridad de ambos paquetes antes de instalar.', nil);
  MediaDownloads.ShowBaseNameInsteadOfUrl := True;
end;

procedure QueuePackage(const BaseUrl, Name, Hash: String);
var
  LocalFile, StagedFile: String;
begin
  LocalFile := ExpandConstant('{src}\') + Name;
  if not FileExists(LocalFile) then begin
    MediaDownloads.Add(BaseUrl + Name, Name, Hash);
    Exit;
  end;
  StagedFile := ExpandConstant('{tmp}\') + Name;
  if not FileCopy(LocalFile, StagedFile, False) then
    RaiseException('No se pudo copiar ' + Name);
  if CompareText(GetSHA256OfFile(StagedFile), Hash) <> 0 then
    RaiseException('El SHA-256 del paquete local no coincide: ' + Name);
end;

procedure Unpack(const Name, Members: String);
var
  ExitCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\tar.exe'), '-xf "' + Name + '" ' + Members,
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('No se pudo iniciar tar.exe de Windows.');
  if ExitCode <> 0 then
    RaiseException('No se pudo extraer ' + Name + ' (código ' + IntToStr(ExitCode) + ').');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  MediaDownloads.Clear;
  MediaDownloads.Show;
  try
    try
      QueuePackage('https://api.nuget.org/v3-flatcontainer/videolan.libvlc.windows/3.0.24/',
        VlcPackage, '3da3f37ee2b2b10e5c788d830a3b513658c36521ea1b0f966ed86eb69f41ef3c');
      QueuePackage('https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-11-14-12-37/',
        FFmpegPackage, '97927ac5391d5e3a11dc3bce679d54f7fde04cf54380a09970c32eea49ba20a2');
      MediaDownloads.Download;
      MediaDownloads.SetText('Extrayendo los componentes verificados…', '');
      Unpack(VlcPackage, 'build/x64');
      Unpack(FFmpegPackage, '{#FFmpegRoot}/bin {#FFmpegRoot}/LICENSE.txt');
    except
      Result := GetExceptionMessage;
    end;
  finally
    MediaDownloads.Hide;
  end;
end;
