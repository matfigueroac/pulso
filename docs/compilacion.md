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
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer\LumenPlayer.iss
```

## Pruebas de cola y reanudación

Desde la raíz, ejecuta `dotnet run --project tests/Playback -c Release` con el SDK indicado en `global.json`. El comando comprueba las transiciones de la cola y los marcadores sin abrir la aplicación y devuelve un código de error si falla una comprobación.
