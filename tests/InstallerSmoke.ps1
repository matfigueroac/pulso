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

& (Join-Path $install 'Speech/FFmpeg/ffprobe.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'ffprobe no arranca.' }
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
