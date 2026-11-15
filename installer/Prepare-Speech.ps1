$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$downloads = Join-Path $root 'work/speech-deps'
$target = Join-Path $root '.tools/speech'
New-Item -ItemType Directory -Path $downloads, "$target/Whisper", "$target/FFmpeg" -Force | Out-Null

function Get-VerifiedFile($Uri, $Name, $Sha256) {
    $file = Join-Path $downloads $Name
    if (!(Test-Path -LiteralPath $file)) {
        Invoke-WebRequest -Uri $Uri -OutFile $file
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Sha256) {
        throw "La suma SHA256 de $Name no coincide con la versión publicada."
    }
    $file
}

$whisper = Get-VerifiedFile `
    'https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.6/whisper-bin-x64.zip' 'whisper.zip' `
    'b07ea0b1b4115a38e1a7b07debf581f0b77d999925f8acb8f39d322b0ba0a822'
$ffmpeg = Get-VerifiedFile `
    'https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-11-14-12-37/ffmpeg-n8.1.3-35-gf1acb0a266-win64-lgpl-shared-8.1.zip' 'ffmpeg.zip' `
    '97927ac5391d5e3a11dc3bce679d54f7fde04cf54380a09970c32eea49ba20a2'
$model = Get-VerifiedFile `
    'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en-q5_1.bin?download=true' 'ggml-base.en-q5_1.bin' `
    '4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f'

Expand-Archive -LiteralPath $whisper -DestinationPath "$downloads/whisper" -Force
Expand-Archive -LiteralPath $ffmpeg -DestinationPath "$downloads/ffmpeg" -Force

$whisperDir = Join-Path $downloads 'whisper/Release'
$whisperFiles = @('whisper-cli.exe', 'whisper.dll', 'ggml-base.dll', 'ggml-cpu.dll', 'ggml.dll')
foreach ($name in $whisperFiles) {
    Copy-Item -LiteralPath (Join-Path $whisperDir $name) -Destination "$target/Whisper"
}
Copy-Item -LiteralPath $model -Destination "$target/Whisper"
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/v1.8.6/LICENSE' -OutFile "$target/Whisper/LICENSE.txt"
Invoke-WebRequest 'https://raw.githubusercontent.com/openai/whisper/v20250625/LICENSE' -OutFile "$target/Whisper/LICENSE-model.txt"

$ffmpegDir = Join-Path $downloads 'ffmpeg/ffmpeg-n8.1.3-35-gf1acb0a266-win64-lgpl-shared-8.1'
Get-ChildItem -LiteralPath "$ffmpegDir/bin" -File | Where-Object {
    $_.Extension -eq '.dll' -or $_.Name -in 'ffmpeg.exe', 'ffprobe.exe'
} | Copy-Item -Destination "$target/FFmpeg"
Copy-Item -LiteralPath "$ffmpegDir/LICENSE.txt" -Destination "$target/FFmpeg"
