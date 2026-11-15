# Lumen Player

Reproductor de video y audio para Windows 10 y 11 de 64 bits. Usa una interfaz WPF y el motor LibVLC 3.0.24.

## Abrir Lumen

Ejecuta `dist/LumenPlayer/LumenPlayer.exe`. Conserva juntos los archivos de la carpeta `dist/LumenPlayer`. El paquete incluye el motor multimedia y el runtime de .NET.

- Abre uno o varios archivos con **Abrir** o arrástralos a la ventana. El botón **+** los agrega a la cola sin interrumpir el video.
- Abre **CC** para elegir una pista integrada o cargar un archivo de subtítulos, como SRT, ASS, SSA o VTT. Con una pista activa, el menú permite adelantarla o atrasarla en pasos de 100 ms.
- Usa **Imagen** para ajustar brillo, contraste, saturación, gamma y tono en vivo. Incluye los estilos Original, Cine, Vívido, Blanco y negro y Realzar sombras.
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

Si utilizas el SDK local del proyecto, sustituye dotnet por .\.tools\dotnet\dotnet.exe.

## Instalador para Windows

Ejecuta `dist/LumenPlayer-0.2.0-Setup-x64.exe` para instalar Lumen Player en tu cuenta. El instalador agrega un acceso al menú Inicio y registra la desinstalación en **Configuración > Aplicaciones**.

Para volver a generar el instalador, publica primero la app y luego compila la receta con Inno Setup 6.7.4. Desde la raíz del proyecto, el compilador local se ejecuta así.

```powershell
.\.tools\inno\ISCC.exe installer\LumenPlayer.iss
```

## Dependencias

- [LibVLC y LibVLCSharp](https://www.videolan.org/developers/vlc.html) de VideoLAN.
- [Inter](https://github.com/rsms/inter/tree/v4.1) bajo SIL Open Font License 1.1. El texto de la licencia está en `src/Assets/Fonts/LICENSE.txt`.
