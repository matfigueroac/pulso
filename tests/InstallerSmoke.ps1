param(
    [Parameter(Mandatory)]
    [System.IO.FileInfo] $Setup
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP) {
    throw 'Ejecuta esta prueba en el entorno temporal de GitHub Actions.'
}

$install = Join-Path $env:RUNNER_TEMP 'PulsoSmoke'
$profile = Join-Path $env:LOCALAPPDATA 'LumenPlayer'
if ((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $profile)) {
    throw 'La prueba requiere una instalación y un perfil nuevos.'
}

$arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=`"$install`""
$setupProcess = Start-Process -FilePath $Setup.FullName -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($setupProcess.ExitCode -ne 0) {
    throw "El instalador terminó con código $($setupProcess.ExitCode)."
}

$expectedFiles = @(
    'LumenPlayer.exe'
    'LICENSE'
    'THIRD-PARTY-NOTICES.md'
    'libvlc/win-x64/libvlc.dll'
    'Speech/FFmpeg/ffprobe.exe'
    'Speech/Whisper/whisper-cli.exe'
    'Speech/Whisper/ggml-small.en-q5_1.bin'
    'Speech/Whisper/ggml-large-v3-turbo-q5_0.bin'
    'Speech/Whisper/ggml-silero-v5.1.2.bin'
    'Speech/Alignment/wav2vec2-base-960h-int8.onnx'
)
foreach ($file in $expectedFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $install $file) -PathType Leaf)) {
        throw "Falta un archivo instalado: $file"
    }
}

# Compare every installed media file with the original locked publish, including plug-ins.
$published = (Resolve-Path (Join-Path $PSScriptRoot '../dist/LumenPlayer')).Path
foreach ($folder in 'libvlc/win-x64', 'Speech/FFmpeg') {
    $referenceFiles = @(Get-ChildItem (Join-Path $published $folder) -Recurse -File)
    $installedFiles = @(Get-ChildItem (Join-Path $install $folder) -Recurse -File)
    if ($installedFiles.Count -ne $referenceFiles.Count -or !$referenceFiles.Count) {
        throw "Inventario inesperado en $folder."
    }
    foreach ($file in $referenceFiles) {
        $relative = [IO.Path]::GetRelativePath($published, $file.FullName)
        if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash (Join-Path $install $relative)).Hash) {
            throw "El archivo instalado difiere del portable: $relative"
        }
    }
}

& (Join-Path $install 'Speech/FFmpeg/ffprobe.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'ffprobe no arranca.' }
$audio = Join-Path $env:RUNNER_TEMP 'pulso-smoke.wav'
& (Join-Path $install 'Speech/FFmpeg/ffmpeg.exe') -v error -f lavfi -i 'sine=duration=1' -ar 16000 -ac 1 -c:a pcm_s16le $audio
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg no generó audio para transcripción.' }
$sampleRate = & (Join-Path $install 'Speech/FFmpeg/ffprobe.exe') -v error -select_streams a:0 -show_entries stream=sample_rate -of csv=p=0 $audio
if ($LASTEXITCODE -ne 0 -or $sampleRate -ne '16000') { throw 'ffprobe no confirmó los 16 kHz del audio generado.' }
& (Join-Path $install 'Speech/Whisper/whisper-cli.exe') --help
if ($LASTEXITCODE -ne 0) { throw 'whisper-cli no arranca.' }

$app = Start-Process -FilePath (Join-Path $install 'LumenPlayer.exe') -WorkingDirectory $install -PassThru -WindowStyle Hidden
try {
    if (!$app.WaitForInputIdle(20000) -or $app.WaitForExit(3000) -or !$app.Responding) {
        throw 'Pulso no inició una sesión de interfaz estable.'
    }
    Write-Host 'Pulso inició en una instalación limpia.'
} finally {
    if (!$app.HasExited) {
        if (!$app.CloseMainWindow() -or !$app.WaitForExit(5000)) {
            Stop-Process -InputObject $app
            $app.WaitForExit()
        }
    }
}

$uninstaller = Join-Path $install 'unins000.exe'
$uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru -WindowStyle Hidden
if ($uninstallProcess.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $install 'LumenPlayer.exe'))) {
    throw 'Falló la desinstalación de Pulso.'
}
Write-Host 'Instalación y desinstalación completas verificadas.'

# A second machine can use the same two upstream downloads without network access.
$vlcName = 'videolan.libvlc.windows.3.0.24.nupkg'
$vlcSidecar = Join-Path $Setup.DirectoryName $vlcName
$ffmpegSidecar = Join-Path $Setup.DirectoryName 'ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1.zip'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
Copy-Item (Join-Path $PSScriptRoot '../work/speech-deps/ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1.zip') $ffmpegSidecar
Set-Content -LiteralPath $vlcSidecar -Value 'paquete de prueba incompleto'
$invalid = Start-Process $Setup.FullName -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if (!$invalid.ExitCode -or (Test-Path (Join-Path $install 'LumenPlayer.exe'))) {
    throw 'El instalador aceptó un paquete local dañado.'
}
Copy-Item (Join-Path $nugetRoot "videolan.libvlc.windows/3.0.24/$vlcName") $vlcSidecar -Force
$offline = Start-Process $Setup.FullName -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($offline.ExitCode -ne 0 -or !(Test-Path (Join-Path $install 'libvlc/win-x64/libvlc.dll'))) {
    throw 'Falló la instalación desde los paquetes locales verificados.'
}
$cleanup = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
if ($cleanup.ExitCode -ne 0) { throw 'Falló la desinstalación de la segunda copia.' }
Write-Host 'Se rechazó el paquete dañado y se instaló con los dos paquetes locales correctos.'
