# Compilar Pulso en Windows

## Generar el portable

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
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer\WindowsDistribution.iss
```

El instalador público incorpora los modelos de voz. Obtiene los motores de reproducción y transcripción de NuGet y de FFmpeg-Builds durante la instalación (unos 215 MiB en total) y valida ambos con SHA-256. Después puedes reproducir y transcribir sin conexión.

Para instalar en otro equipo sin internet, guarda estos dos archivos junto al instalador, con sus nombres originales. El instalador los verifica antes de modificar la instalación.

- [VideoLAN.LibVLC.Windows 3.0.24 de NuGet](https://api.nuget.org/v3-flatcontainer/videolan.libvlc.windows/3.0.24/videolan.libvlc.windows.3.0.24.nupkg)
- [FFmpeg 8.1.3 para Windows x64 de FFmpeg-Builds](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-11-14-12-37/ffmpeg-n8.1.3-35-gf1acb0a266-win64-lgpl-shared-8.1.zip)

## Pruebas de cola y reanudación

Desde la raíz, ejecuta `dotnet run --project tests/Playback -c Release` con el SDK indicado en `global.json`. El comando comprueba las transiciones de la cola y los marcadores sin abrir la aplicación y devuelve un código de error si falla una comprobación.
