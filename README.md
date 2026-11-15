# Lumen Player

Reproductor de video y audio para Windows 10 y 11 de 64 bits. Usa una interfaz WPF y el motor LibVLC 3.0.24.

## Abrir Lumen

Ejecuta `dist/LumenPlayer/LumenPlayer.exe`. Conserva juntos los archivos de la carpeta `dist/LumenPlayer`. El paquete incluye el motor multimedia y el runtime de .NET.

- Abre uno o varios archivos con **Abrir** o arrástralos a la ventana. El botón **+** los agrega a la cola sin interrumpir el video.
- Pulsa **CC** para reconocer el diálogo en inglés y mostrarlo sobre el video mientras se reproduce. La transcripción funciona en tu equipo y puede tardar unos segundos al empezar o saltar a otra escena.
- Abre la flecha junto a **CC** para elegir una pista integrada o cargar un archivo de subtítulos. **Generar subtítulos automáticos (SRT)** procesa el archivo completo. Usa **Guardar copia del SRT** para conservar el resultado.
- Haz clic derecho sobre el video para pausar, cambiar de pista, elegir un estilo de imagen o guardar un fotograma. También puedes abrir el menú con Mayús+F10.
- Usa **Imagen** para ajustar brillo, contraste, saturación, gamma y tono en vivo. Los estilos cambian con una transición suave durante la reproducción. Usa **Ver original** para compararlos sin perder tus ajustes.
- En **Imagen → Relación de aspecto** puedes consultar las proporciones originales y cambiar el formato. El botón **Sonido y ecualizador**, junto al volumen, ofrece seis presets y refuerzo de hasta +9 dB.
- En **Opciones de reproducción** encontrarás pistas de audio, velocidades de 0,25× a 3×, proporción de video, desentrelazado y captura de fotogramas en PNG.
- La pantalla completa oculta los controles tras unos segundos de reproducción. Mueve el mouse para volver a verlos.

La compatibilidad depende del códec y la protección del archivo. Se comprobó la reproducción con MP4/H.264/AAC, MKV con SRT integrado, WebM/VP9/Opus y FLAC. La decodificación por software mantiene los filtros de imagen y subtítulos compatibles con la salida de video probada en Windows.

| Acción | Atajo |
| --- | --- |
| Abrir archivos | Ctrl+O |
| Agregar a la cola | Ctrl+Mayús+O |
| Reproducir o pausar | Espacio |
| Saltar 5 segundos | ← o → |
| Silenciar | M |
| Alternar pantalla completa | F o Alt+Enter |
| Salir de pantalla completa | Esc |
| Cargar subtítulos | Ctrl+T |
| Mostrar u ocultar panel lateral | Ctrl+L |
| Ajustar imagen | Ctrl+E |
| Guardar fotograma | Ctrl+S |

## Compilar

Usa el SDK .NET 10 indicado en `global.json`. Desde la raíz del proyecto, ejecuta lo siguiente.

```powershell
dotnet publish src/LumenPlayer.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o dist/LumenPlayer
```

Ejecuta primero `installer/Prepare-Speech.ps1` para incluir la generación de subtítulos en el paquete. El script descarga y verifica el modelo y los ejecutables. La transcripción funciona sin conexión una vez instalados.

Si utilizas el SDK local del proyecto, sustituye dotnet por .\.tools\dotnet\dotnet.exe.

## Instalador para Windows

Ejecuta `dist/LumenPlayer-0.4.0-Setup-x64.exe` para instalar o actualizar Lumen Player en tu cuenta. El ejecutable permanece en `%LOCALAPPDATA%\Programs\Lumen Player\LumenPlayer.exe`.

Para volver a generar el instalador, publica primero la app y luego compila la receta con Inno Setup 6.7.4. Desde la raíz del proyecto, el compilador local se ejecuta así.

```powershell
.\.tools\inno\ISCC.exe installer\LumenPlayer.iss
```

## Dependencias

- [LibVLC y LibVLCSharp](https://www.videolan.org/developers/vlc.html) de VideoLAN.
- [Inter](https://github.com/rsms/inter/tree/v4.1) bajo SIL Open Font License 1.1. El texto de la licencia está en `src/Assets/Fonts/LICENSE.txt`.
- [whisper.cpp 1.8.6](https://github.com/ggml-org/whisper.cpp/tree/v1.8.6) y [modelo Whisper base.en Q5](https://huggingface.co/ggerganov/whisper.cpp/blob/main/ggml-base.en-q5_1.bin), con licencias MIT incluidas en `Speech/Whisper`.
- [FFmpeg LGPL compartido para Windows](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-11-14-12-37), con [código fuente](https://github.com/FFmpeg/FFmpeg/tree/f1acb0a266) y licencia incluida en `Speech/FFmpeg`. Lumen lo ejecuta como herramienta separada para extraer el audio.
