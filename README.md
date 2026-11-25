# Pulso

Reproductor de video y audio para Windows x64 con cola de reproducción, reanudación por archivo y subtítulos en inglés generados en el equipo. Usa WPF y LibVLC 3.0.24.

Consulta los [resultados de la auditoría](docs/auditoria/README.md) para ver capturas y mediciones de reproducción 4K.

La [beta 0.4.31](https://github.com/matfigueroac/pulso/releases/tag/v0.4.31) está disponible como código fuente. Sigue las instrucciones de [compilación](#compilar) para generar el portable o el instalador en Windows.

## Abrir Pulso

En el paquete portable, ejecuta `LumenPlayer.exe` desde la carpeta extraída y conserva juntos los archivos. El paquete incluye el motor multimedia y el runtime de .NET. Si compilas desde el código fuente, el comando de publicación deja el portable en `dist/LumenPlayer`.

- Abre uno o varios archivos con **Abrir archivos** o arrástralos al reproductor. Para agregarlos sin interrumpir la reproducción, suéltalos sobre la cola o usa **Agregar archivos**. Si abres varios videos junto con sus subtítulos, Pulso empareja los nombres y da prioridad a las coincidencias dentro de la misma carpeta (por ejemplo, `Viaje.mkv` y `Viaje.es.srt`). Los subtítulos con otro nombre acompañan al primer archivo.
- Al volver a abrir una película, la reproducción sigue desde donde la dejaste y recupera los CC, la pista de subtítulos elegida o el estado desactivado. Si la película llegó al final, la próxima vez se reproduce desde el inicio.
- Pulsa **CC** para reconocer el diálogo de la pista de audio en inglés y mostrarlo en una o dos líneas sincronizadas con la voz. La transcripción funciona en tu equipo y puede tardar unos segundos al empezar o saltar a otra escena.
- Para los CC en vivo, Pulso usa el canal central de las pistas 5.1 para reducir música y efectos. Al unir ventanas de audio, compara las palabras y sus tiempos para reducir repeticiones sin cambiar una frase que ya apareció en pantalla.
- Abre la flecha junto a **CC** para elegir una pista integrada o cargar un archivo de subtítulos. **Generar y guardar SRT junto al video** prioriza la pista de diálogo en inglés. En 5.1 y 7.1 da más peso al centro, conserva el audio de los laterales y omite el canal de graves. Pulso revisa si falta la primera palabra después de una pausa, alinea las frases sobre la misma extracción de audio y ajusta el tiempo de lectura. Si el SRT de una pista estéreo tiene un silencio largo al principio, Pulso vuelve a buscar voces bajas con Whisper Large V3 Turbo y las sincroniza con la pista. El resultado se guarda junto al video, por ejemplo `Película.mkv.lumen.v8.en.srt`. **Generar SRT mejorado** conserva la versión anterior. También puedes mostrar el SRT en el Explorador o guardar una copia en otra carpeta.
- En **CC → Sincronización** puedes adelantar o atrasar los subtítulos mientras se reproduce el video. Pulso recuerda por película el ajuste de los CC en vivo y el de cada pista. Los CC en vivo admiten hasta 10 segundos en cada dirección.
- Haz clic derecho sobre el video para pausar, cambiar de pista, elegir un estilo de imagen o guardar un fotograma. También puedes abrir el menú con Mayús+F10.
- Usa **Imagen** para ajustar brillo, contraste, saturación, gamma y tono en vivo. Los estilos cambian con una transición suave durante la reproducción. Usa **Ver original** para compararlos sin perder tus ajustes.
- En **Imagen → Relación de aspecto** puedes consultar las proporciones originales y cambiar el formato. El botón **Sonido y ecualizador**, junto al volumen, ofrece seis presets y refuerzo de hasta +9 dB.
- En **Opciones de reproducción** encontrarás pistas de audio, velocidades de 0,25× a 3×, proporción de video, desentrelazado y captura de fotogramas en PNG.
- Para reducir el uso de CPU y memoria al reproducir 4K, activa **Aceleración por hardware (próximo archivo)** en **Opciones de reproducción**. Pulso recuerda la preferencia y la aplica la próxima vez que abras o vuelvas a reproducir un archivo. El modo acelerado desactiva los ajustes de color para evitar incompatibilidades con el decodificador.
- El botón **Abrir panel lateral** está en la barra inferior, junto al de pantalla completa. En pantalla completa, acerca el mouse al borde inferior para mostrar la barra. Usa **Barra en pantalla completa** en el engranaje o con clic derecho sobre el video para dejar la barra **Siempre visible**. Pulso recuerda la opción elegida.
- **Ajustar al ancho** es el encuadre inicial de los videos horizontales. En **Encuadre del video** también puedes mostrar todos los bordes, llenar el área o elegir una proporción. Los ajustes de imagen y subtítulos se abren en el panel derecho. Ciérralo con **Esc**.
- En el menú junto a **CC**, abre **Ajustes de subtítulos** para sincronizar la pista activa y cambiar tamaño, color, opacidad, posición vertical, grosor del borde negro y opacidad de la sombra de los SRT y los CC mientras ves el video. **Guardar como predeterminado** adopta el estilo actual para los videos sin ajustes propios. **Usar predeterminado** hace que la película actual siga ese estilo, incluso si lo cambias más adelante.

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
| Adelantar o atrasar subtítulos 100 ms | Ctrl+← o Ctrl+→ |
| Restablecer sincronización | Ctrl+0 |
| Mostrar u ocultar panel lateral | Ctrl+L |
| Ajustar imagen | Ctrl+E |
| Guardar fotograma | Ctrl+S |

## Pruebas de cola y reanudación

Desde la raíz, ejecuta `dotnet run --project tests/Playback -c Release` con el SDK indicado en `global.json`. El comando comprueba las transiciones de la cola y los marcadores sin abrir la aplicación y devuelve un código de error si falla una comprobación.

## Compilar

En Windows x64, usa el SDK .NET 10 indicado en `global.json`. Desde la raíz del proyecto, ejecuta lo siguiente.

```powershell
dotnet publish src/LumenPlayer.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o dist/LumenPlayer
```

Para incluir los CC en vivo y la generación de SRT, ejecuta `installer/Prepare-Speech.ps1` antes de publicar. El script descarga los modelos y las herramientas de transcripción y verifica sus hashes SHA-256. La transcripción funciona sin conexión una vez instalados. La verificación de Windows en GitHub genera el portable básico, sin los modelos de voz, y ejecuta las pruebas de cola y reanudación.

Si utilizas el SDK local del proyecto, sustituye dotnet por .\.tools\dotnet\dotnet.exe.

## Instalador para Windows

En `dist`, ejecuta el instalador `Pulso-<versión>-Setup-x64.exe` de la versión que quieras instalar. También permite actualizar Lumen Player en tu cuenta. El ejecutable permanece en `%LOCALAPPDATA%\Programs\Lumen Player\LumenPlayer.exe`.

Para generar el instalador, publica primero la app y luego compila la receta con Inno Setup 6.7.4. Para el instalador con transcripción, ejecuta `installer/Prepare-Speech.ps1` antes de publicar la app. Con Inno Setup instalado en su ubicación predeterminada, ejecuta desde la raíz del proyecto.

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer\LumenPlayer.iss
```

## Dependencias

- [LibVLC y LibVLCSharp](https://www.videolan.org/developers/vlc.html) de VideoLAN.
- [Inter](https://github.com/rsms/inter/tree/v4.1) bajo SIL Open Font License 1.1. El texto de la licencia está en `src/Assets/Fonts/LICENSE.txt`.
- [whisper.cpp 1.8.6](https://github.com/ggml-org/whisper.cpp/tree/v1.8.6) y [modelo Whisper small.en Q5](https://huggingface.co/ggerganov/whisper.cpp/blob/f281eb4/ggml-small.en-q5_1.bin), con licencias MIT incluidas en `Speech/Whisper`.
- [Silero VAD 5.1.2](https://github.com/snakers4/silero-vad/tree/v5.1.2) detecta la voz para transcribir y sincronizar los CC en vivo y el SRT. Su licencia MIT está en `Speech/Whisper`.
- [WhisperX](https://github.com/m-bain/whisperX) y [Wav2Vec2 Base 960h para ONNX](https://huggingface.co/Xenova/wav2vec2-base-960h) se usan para la alineación de palabras. Sus licencias están en `Speech/Alignment`.
- [FFmpeg LGPL compartido para Windows](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-11-14-12-37), con [código fuente](https://github.com/FFmpeg/FFmpeg/tree/f1acb0a266) y licencia incluida en `Speech/FFmpeg`. Pulso lo ejecuta como herramienta separada para extraer el audio.

## Licencia

El código propio de Pulso se distribuye bajo [MIT](LICENSE). Consulta los [avisos de terceros](THIRD-PARTY-NOTICES.md) para conocer las licencias de las dependencias y preparar una distribución pública de binarios.
