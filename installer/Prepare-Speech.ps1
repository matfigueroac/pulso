$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$downloads = Join-Path $root 'work/speech-deps'
$target = Join-Path $root '.tools/speech'
New-Item -ItemType Directory -Path $downloads, "$target/Whisper", "$target/FFmpeg", "$target/Alignment" -Force | Out-Null

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
    'https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1.zip' 'ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1.zip' `
    '5cef7bc48e513f3cee7c881e8b2999d835541edb1846c3e89443a67f8f8556ab'
$model = Get-VerifiedFile `
    'https://huggingface.co/ggerganov/whisper.cpp/resolve/f281eb4/ggml-small.en-q5_1.bin?download=true' 'ggml-small.en-q5_1.bin' `
    'bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30'
$dialogueReviewModel = Get-VerifiedFile `
    'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin?download=true' 'ggml-large-v3-turbo-q5_0.bin' `
    '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2'
$voiceModel = Get-VerifiedFile `
    'https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin?download=true' 'ggml-silero-v5.1.2.bin' `
    '29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf'
$alignmentModel = Get-VerifiedFile `
    'https://huggingface.co/Xenova/wav2vec2-base-960h/resolve/a19f851b3d42865797e410752b4c570c871e4825/onnx/model_quantized.onnx?download=true' `
    'wav2vec2-base-960h-int8.onnx' 'cd5040c147381580ed73258143dd8e0c28e800a09e74ee42ee2b3e8cb4d760a3'
Copy-Item -LiteralPath $alignmentModel -Destination "$target/Alignment"
Invoke-WebRequest 'https://raw.githubusercontent.com/m-bain/whisperX/b904f02b7e1f26fd10fb04840d655ff27e88e464/LICENSE' `
    -OutFile "$target/Alignment/LICENSE-WhisperX.txt"
Invoke-WebRequest 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile "$target/Alignment/LICENSE-Wav2Vec2.txt"

Expand-Archive -LiteralPath $whisper -DestinationPath "$downloads/whisper" -Force
Expand-Archive -LiteralPath $ffmpeg -DestinationPath "$downloads/ffmpeg" -Force

$whisperDir = Join-Path $downloads 'whisper/Release'
$whisperFiles = @('whisper-cli.exe', 'whisper.dll', 'ggml-base.dll', 'ggml-cpu.dll', 'ggml.dll')
foreach ($name in $whisperFiles) {
    Copy-Item -LiteralPath (Join-Path $whisperDir $name) -Destination "$target/Whisper"
}
Copy-Item -LiteralPath $model -Destination "$target/Whisper"
Copy-Item -LiteralPath $dialogueReviewModel -Destination "$target/Whisper"
Copy-Item -LiteralPath $voiceModel -Destination "$target/Whisper"
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/v1.8.6/LICENSE' -OutFile "$target/Whisper/LICENSE.txt"
Invoke-WebRequest 'https://raw.githubusercontent.com/openai/whisper/v20250625/LICENSE' -OutFile "$target/Whisper/LICENSE-model.txt"
Invoke-WebRequest 'https://raw.githubusercontent.com/snakers4/silero-vad/v5.1.2/LICENSE' -OutFile "$target/Whisper/LICENSE-Silero.txt"

$ffmpegDir = Join-Path $downloads 'ffmpeg/ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1'
Get-ChildItem -LiteralPath "$ffmpegDir/bin" -File | Where-Object {
    $_.Extension -eq '.dll' -or $_.Name -in 'ffmpeg.exe', 'ffprobe.exe'
} | Copy-Item -Destination "$target/FFmpeg"
Copy-Item -LiteralPath "$ffmpegDir/LICENSE.txt" -Destination "$target/FFmpeg"
