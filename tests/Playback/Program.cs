using LumenPlayer;

static void Check(bool result, string test)
{
    if (!result) throw new InvalidOperationException(test);
    Console.WriteLine("OK " + test);
}

var queue = new PlaybackQueue<string>();
foreach (var title in new[] { "A", "B", "C" }) queue.Items.Add(title);

var a = queue.Begin("A");
Check(queue.AfterEnd() == "B", "Finalizar avanza al siguiente");
var b = queue.Begin("B");
Check(!queue.CanStart(a) && queue.CanStart(b), "Cambiar durante la carga invalida el inicio anterior");
a.OnPlaying(null, EventArgs.Empty);
a.OnError(null, EventArgs.Empty);
Check(!b.Played && !b.Failed, "Eventos tardíos del anterior no afectan al nuevo");

var resume = new ResumePosition();
resume.Restore(new PlaybackBookmark(36000, "off"), 0);
resume.Observe(0, 100);
Check(resume.Position == 36000 && resume.Bookmark(b, false, "off") is null,
    "Una carga interrumpida conserva el marcador previo");
b.OnPlaying(null, EventArgs.Empty);
resume.Observe(36200, 300);
resume.Seek(51000, 400);
resume.Observe(36200, 500);
Check(resume.Bookmark(b, false, "off") == new PlaybackBookmark(51000, "off"),
    "Al quitar mientras se completa un salto se conserva el destino");
Check(resume.Bookmark(b, true, "off") == new PlaybackBookmark(0, "off"),
    "Finalizar reinicia la posición y conserva los subtítulos");
b.OnError(null, EventArgs.Empty);
Check(resume.Bookmark(b, true, "off") is null && resume.Bookmark(b, false, "off") is null,
    "Un error de reproducción conserva el marcador guardado aunque VLC haya indicado inicio");

Check(!queue.Remove("A").RemovedCurrent && queue.Request == b, "Quitar otro video conserva la reproducción");
var removal = queue.Remove("B");
Check(removal.Replacement == "C" && queue.Request is null && !queue.CanStart(b),
    "Quitar el actual propone el vecino e invalida su carga");
queue.Items.Add("D");
queue.Begin("D");
Check(queue.AfterEnd() is null, "El último termina sin repetición");
queue.CycleRepeat();
Check(queue.AfterEnd() == "C" && queue.Relative(1) == "C", "Repetir cola vuelve al primero");
queue.CycleRepeat();
Check(queue.AfterEnd() == "D" && queue.Relative(-1) == "C", "Repetir actual permite navegación manual");
Check(queue.Remove("D").Replacement == "C", "Quitar el último activo propone el anterior");
queue.Begin("C");
Check(queue.Remove("C").Replacement is null && queue.Current is null && queue.Items.Count == 0,
    "Quitar el único archivo deja la cola vacía");
queue.Items.Add("A");
queue.Items.Add("B");
var pending = queue.Begin("A");
queue.Stop();
Check(!queue.CanStart(pending) && queue.Remove("A").Replacement is null,
    "Detener durante la carga y quitar después no arranca otro video");

var folderA = Path.Combine("biblioteca", "uno");
var folderB = Path.Combine("biblioteca", "dos");
var media = new[]
{
    Path.Combine(folderA, "Viaje.mp4"),
    Path.Combine(folderB, "Viaje.mp4"),
    Path.Combine(folderB, "Viaje.mkv"),
    Path.Combine(folderB, "Viaje.2.mp4")
};
Check(SubtitlePairing.FindOwner(Path.Combine(folderB, "VIAJE.es.srt"), media) == 1,
    "Los subtítulos con nombres iguales prefieren el video de su carpeta");
Check(SubtitlePairing.FindOwner(Path.Combine(folderB, "Viaje.mkv.es.srt"), media) == 2,
    "El nombre con extensión distingue dos formatos del mismo video");
Check(SubtitlePairing.FindOwner(Path.Combine(folderB, "Viaje.2.es.srt"), media) == 3,
    "El título más largo coincide con el episodio correcto");
Check(SubtitlePairing.FindOwner(Path.Combine(folderB, "Diálogo.srt"), media) == 0,
    "Un subtítulo elegido con nombre distinto conserva la asociación manual al primer archivo");
