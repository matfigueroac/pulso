# Licencias de terceros de Pulso

La licencia [MIT](LICENSE) cubre el código propio de Pulso. Las dependencias, los modelos y la fuente tipográfica conservan sus licencias originales. Estas rutas corresponden al paquete portable y al directorio de instalación.

| Componente | Origen | Licencia y avisos en el paquete |
| --- | --- | --- |
| LibVLC para Windows, 3.0.24 | [Paquete de VideoLAN](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24) y [fuentes de VLC 3.0.24](https://download.videolan.org/pub/videolan/vlc/3.0.24/) | El paquete se declara LGPL-2.1-or-later. Los módulos y las bibliotecas incorporadas mantienen sus términos. `Licenses/LGPL-2.1.txt`, `Licenses/GPL-2.0.txt` y `Licenses/VLC-AUTHORS.txt`. |
| LibVLCSharp y LibVLCSharp.WPF, 3.10.2 | [Commit registrado en NuGet](https://github.com/videolan/libvlcsharp/tree/3082c09ca955131c705fc4767fde03e9c4a14999) | LGPL-2.1-or-later, `Licenses/LGPL-2.1.txt`. |
| .NET y Windows Desktop | [dotnet/runtime](https://github.com/dotnet/runtime) y [avisos de WPF 10.0.15](https://github.com/dotnet/wpf/blob/v10.0.15/THIRD-PARTY-NOTICES.TXT) | MIT y avisos de terceros. En la publicación autocontenida, `Licenses/dotnet/`. |
| System.Numerics.Tensors | [Paquete de Microsoft](https://www.nuget.org/packages/System.Numerics.Tensors/9.0.0) | MIT, `Licenses/System.Numerics.Tensors-LICENSE.txt` y `Licenses/System.Numerics.Tensors-ThirdPartyNotices.txt`. |
| ONNX Runtime y ONNX Runtime Managed, 1.23.2 | [Commit registrado en NuGet](https://github.com/microsoft/onnxruntime/tree/a83fc4d58cb48eb68890dd689f94f28288cf2278) | MIT y avisos compartidos por ambos paquetes, `Licenses/ONNXRuntime-LICENSE.txt` y `Licenses/ONNXRuntime-ThirdPartyNotices.txt`. |
| Inter 4.1 | [Inter](https://github.com/rsms/inter/tree/v4.1) | SIL Open Font License 1.1, `Licenses/Inter-OFL.txt`. |
| WhisperX | [Referencia para la alineación](https://github.com/m-bain/whisperX/tree/b904f02b7e1f26fd10fb04840d655ff27e88e464) | BSD-2-Clause, `Licenses/WhisperX-BSD-2-Clause.txt`. Pulso implementa la alineación en C#. |

## Componentes opcionales de transcripción

`installer/Prepare-Speech.ps1` registra las URL y las sumas SHA-256 de los archivos descargados.

| Componente | Origen | Licencia y avisos en el paquete |
| --- | --- | --- |
| whisper.cpp 1.8.6 | [Versión oficial](https://github.com/ggml-org/whisper.cpp/tree/v1.8.6) | MIT, `Speech/Whisper/LICENSE.txt`. |
| Whisper small.en y large-v3-turbo | [Whisper](https://github.com/openai/whisper/tree/v20250625), con modelos [small.en Q5](https://huggingface.co/ggerganov/whisper.cpp/blob/f281eb4/ggml-small.en-q5_1.bin) y [large-v3-turbo Q5](https://huggingface.co/ggerganov/whisper.cpp/blob/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin) convertidos para whisper.cpp. | MIT, `Speech/Whisper/LICENSE-model.txt`. |
| Silero VAD 5.1.2 | [Código original](https://github.com/snakers4/silero-vad/tree/v5.1.2) y [conversión GGML](https://huggingface.co/ggml-org/whisper-vad) | MIT, `Speech/Whisper/LICENSE-Silero.txt`. |
| Wav2Vec2 Base 960h | [Modelo de Meta](https://huggingface.co/facebook/wav2vec2-base-960h), convertido a ONNX y cuantizado en [Xenova](https://huggingface.co/Xenova/wav2vec2-base-960h/tree/a19f851b3d42865797e410752b4c570c871e4825) | Apache-2.0, `Speech/Alignment/LICENSE-Wav2Vec2.txt`. |
| FFmpeg, compilación compartida para Windows x64 | [Binario](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-11-14-12-37), [receta del proveedor](https://github.com/FFmpeg-Builds/FFmpeg-Builds/tree/c9fe5114060e507409f7ee07edfd82efaf25d767) y [revisión de FFmpeg f1acb0a266](https://github.com/FFmpeg/FFmpeg/tree/f1acb0a266) | LGPL-3.0-or-later para esta compilación. `Speech/FFmpeg/LICENSE.txt` y `Licenses/GPL-3.0.txt`. Pulso ejecuta FFmpeg y FFprobe como procesos separados. |

## Distribución de binarios

La primera publicación puede compartir el código fuente, las fuentes tipográficas con su licencia y las instrucciones de compilación. El SDK, NuGet y el script de voz obtienen las demás dependencias durante la preparación local.

Las 323 DLL de LibVLC en el portable 0.4.31 coinciden por SHA-256 con las del [ZIP oficial de VLC 3.0.24 para Windows x64](https://download.videolan.org/pub/videolan/vlc/3.0.24/win64/vlc-3.0.24-win64.zip). `Licenses/VLC-AUTHORS.txt` y `Licenses/GPL-2.0.txt` proceden de ese archivo.

Al distribuir un portable o instalador, acompaña los binarios de LibVLC, LibVLCSharp y, si se incluye, FFmpeg con su código fuente correspondiente. Incluye las bibliotecas y módulos cubiertos, los cambios y las instrucciones necesarias para reproducir las compilaciones. Los enlaces de este documento identifican los orígenes. Verifica el contenido de cada paquete y ofrece el código fuente junto con la descarga del binario. Conserva las DLL separadas para que puedan reemplazarse por compilaciones compatibles. [GNU LGPL 2.1](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html) · [GNU LGPL 3](https://www.gnu.org/licenses/lgpl-3.0.html) · [Guía de distribución de FFmpeg](https://ffmpeg.org/legal.html)
