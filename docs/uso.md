# Guía de uso

En Windows x64, ejecuta el instalador y abre **Pulso** desde Inicio. Si compilas el portable, abre `LumenPlayer.exe` en `dist/LumenPlayer` y conserva juntos sus archivos.

## Archivos y cola

- Abre uno o varios archivos con **Abrir archivos** o arrástralos al reproductor. Para agregarlos sin interrumpir la reproducción, suéltalos sobre la cola o usa **Agregar archivos**. Si abres varios videos junto con sus subtítulos, Pulso empareja los nombres y da prioridad a las coincidencias dentro de la misma carpeta (por ejemplo, `Viaje.mkv` y `Viaje.es.srt`). Los subtítulos con otro nombre acompañan al primer archivo.
- Al volver a abrir una película, la reproducción sigue desde donde la dejaste y recupera los CC, la pista de subtítulos elegida o el estado desactivado. Si la película llegó al final, la próxima vez se reproduce desde el inicio.
## Subtítulos y transcripción

- Pulsa **CC** para reconocer el diálogo de la pista de audio en inglés y mostrarlo en una o dos líneas sincronizadas con la voz. La transcripción funciona en tu equipo y puede tardar unos segundos al empezar o saltar a otra escena.
- Para los CC en vivo, Pulso usa el canal central de las pistas 5.1 para reducir música y efectos. Al unir ventanas de audio, compara las palabras y sus tiempos para reducir repeticiones sin cambiar una frase que ya apareció en pantalla.
- Abre la flecha junto a **CC** para elegir una pista integrada o cargar un archivo de subtítulos. **Generar y guardar SRT junto al video** prioriza la pista de diálogo en inglés. En 5.1 y 7.1 da más peso al centro, conserva el audio de los laterales y omite el canal de graves. Pulso revisa si falta la primera palabra después de una pausa, alinea las frases sobre la misma extracción de audio y ajusta el tiempo de lectura. Si el SRT de una pista estéreo tiene un silencio largo al principio, Pulso vuelve a buscar voces bajas con Whisper Large V3 Turbo y las sincroniza con la pista. El resultado se guarda junto al video, por ejemplo `Película.mkv.lumen.v8.en.srt`. **Generar SRT mejorado** conserva la versión anterior. También puedes mostrar el SRT en el Explorador o guardar una copia en otra carpeta.
- En **CC → Sincronización** puedes adelantar o atrasar los subtítulos mientras se reproduce el video. Pulso recuerda por película el ajuste de los CC en vivo y el de cada pista. Los CC en vivo admiten hasta 10 segundos en cada dirección.
## Imagen, sonido y controles

- Haz clic derecho sobre el video para pausar, cambiar de pista, elegir un estilo de imagen o guardar un fotograma. También puedes abrir el menú con Mayús+F10.
- Usa **Imagen** para ajustar brillo, contraste, saturación, gamma y tono en vivo. Los estilos cambian con una transición suave durante la reproducción. Usa **Ver original** para compararlos sin perder tus ajustes.
- En **Imagen → Relación de aspecto** puedes consultar las proporciones originales y cambiar el formato. El botón **Sonido y ecualizador**, junto al volumen, ofrece seis presets y refuerzo de hasta +9 dB.
- En **Opciones de reproducción** encontrarás pistas de audio, velocidades de 0,25× a 3×, proporción de video, desentrelazado y captura de fotogramas en PNG.
- Para reducir el uso de CPU y memoria al reproducir 4K, activa **Aceleración por hardware (próximo archivo)** en **Opciones de reproducción**. Pulso recuerda la preferencia y la aplica la próxima vez que abras o vuelvas a reproducir un archivo. El modo acelerado desactiva los ajustes de color para evitar incompatibilidades con el decodificador.
- El botón **Abrir panel lateral** está en la barra inferior, junto al de pantalla completa. En pantalla completa, acerca el mouse al borde inferior para mostrar la barra. Usa **Barra en pantalla completa** en el engranaje o con clic derecho sobre el video para dejar la barra **Siempre visible**. Pulso recuerda la opción elegida.
- **Ajustar al ancho** es el encuadre inicial de los videos horizontales. En **Encuadre del video** también puedes mostrar todos los bordes, llenar el área o elegir una proporción. Los ajustes de imagen y subtítulos se abren en el panel derecho. Ciérralo con **Esc**.
- En el menú junto a **CC**, abre **Ajustes de subtítulos** para sincronizar la pista activa y cambiar tamaño, color, opacidad, posición vertical, grosor del borde negro y opacidad de la sombra de los SRT y los CC mientras ves el video. **Guardar como predeterminado** adopta el estilo actual para los videos sin ajustes propios. **Usar predeterminado** hace que la película actual siga ese estilo, incluso si lo cambias más adelante.

La compatibilidad depende del códec y la protección del archivo. Se comprobó la reproducción con MP4/H.264/AAC, MKV con SRT integrado, WebM/VP9/Opus y FLAC. La decodificación por software mantiene los filtros de imagen y subtítulos compatibles con la salida de video probada en Windows.

## Atajos de teclado

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
