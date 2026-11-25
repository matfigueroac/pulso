using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LumenPlayer;

public partial class MainWindow
{
    private CancellationTokenSource? _subtitleGeneration;

    private IEnumerable<Control> BuildAutomaticSubtitleItems()
    {
        if (_subtitleGeneration is not null)
        {
            yield return Command("Cancelar generación del SRT", () => _subtitleGeneration?.Cancel(),
                enabled: !_subtitleGeneration.IsCancellationRequested);
            yield break;
        }

        var entry = _current;
        if (entry is null)
            yield break;
        var cached = SpeechRecognition.FindGeneratedSubtitle(entry.Location);

        if (cached is not null)
        {
            yield return Command("Cargar SRT generado (inglés)", () => AttachSubtitles([cached]));
            yield return Command("Mostrar SRT generado en el Explorador", () => ShowSubtitleFile(cached));
            yield return Command("Guardar copia del SRT…", () => SaveGeneratedSubtitles(entry, cached));
        }
        if (!File.Exists(SpeechRecognition.GetSubtitlePath(entry.Location)))
        {
            yield return SpeechRecognition.IsAvailable
                ? Command(cached is null ? "Generar y guardar SRT junto al video…" : "Generar SRT mejorado…",
                    () => GenerateSubtitles(entry))
                : Command("Motor de subtítulos no disponible", () => { }, enabled: false);
        }

        _subtitleFiles.TryGetValue(CurrentSubtitleTrack, out var active);
        var adjacent = FindAdjacentSrt(entry.Location);
        var external = File.Exists(active) && Path.GetExtension(active).Equals(".srt", StringComparison.OrdinalIgnoreCase)
            ? active : adjacent;
        if (external is not null && !string.Equals(external, cached, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(external, active, StringComparison.OrdinalIgnoreCase))
                yield return Command("Cargar " + Path.GetFileName(external), () => AttachSubtitles([external]));
            yield return Command("Mostrar SRT en el Explorador", () => ShowSubtitleFile(external));
        }
    }

    private void ShowSubtitleFile(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("El archivo SRT ya no está en esa ubicación.", error: true);
            return;
        }
        try
        {
            Process.Start("explorer.exe", "/select,\"" + path + "\"")?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            SetStatus("No se pudo abrir el Explorador.", error: true);
            Trace.TraceWarning(ex.Message);
        }
    }

    private async void GenerateSubtitles(QueueEntry entry)
    {
        if (_subtitleGeneration is not null)
            return;
        _pendingSubtitleMode = null;
        SetLiveCaptions(false, announce: false);
        using var generation = new CancellationTokenSource();
        _subtitleGeneration = generation;
        var progress = new Progress<string>(message =>
        {
            if (ReferenceEquals(_subtitleGeneration, generation) && !generation.IsCancellationRequested)
            {
                SetStatus(message);
                SubtitleOptions.ToolTip = message;
            }
        });
        try
        {
            var target = SpeechRecognition.GetSubtitlePath(entry.Location);
            var ordinal = GetAudioOrdinal(_player?.AudioTrack ?? -1);
            await SpeechRecognition.Generate(entry.Location, target, ordinal, progress, generation.Token);
            generation.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(_current, entry))
            {
                AttachSubtitles([target]);
                if (entry.Subtitles.Contains(target))
                    SetStatus("SRT guardado junto al video · " + Path.GetFileName(target));
            }
            else
            {
                entry.Subtitles.Add(target);
                SetStatus("SRT guardado junto a " + entry.Title);
            }
        }
        catch (OperationCanceledException)
        {
            if (_ready && !_liveCaptions)
                SetStatus("Generación de subtítulos cancelada");
        }
        catch (InvalidDataException ex)
        {
            SetStatus(ex.Message, error: true);
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("No hay permiso para guardar el SRT en la carpeta del video.", error: true);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or System.Text.Json.JsonException or InvalidOperationException)
        {
            SetStatus("No se pudieron generar los subtítulos en inglés.", error: true);
            Trace.TraceWarning(ex.Message);
        }
        finally
        {
            _subtitleGeneration = null;
            SubtitleOptions.ToolTip = "Opciones de subtítulos";
        }
    }

    private void SaveGeneratedSubtitles(QueueEntry entry, string source)
    {
        var picker = new SaveFileDialog
        {
            Title = "Guardar subtítulos en inglés",
            Filter = "Subtítulos SRT|*.srt",
            DefaultExt = ".srt",
            InitialDirectory = Path.GetDirectoryName(entry.Location),
            FileName = Path.GetFileNameWithoutExtension(entry.Title) + ".en.srt"
        };
        if (picker.ShowDialog(this) != true)
            return;
        try
        {
            File.Copy(source, picker.FileName, overwrite: true);
            SetStatus("Subtítulos guardados");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("No se pudieron guardar los subtítulos.", error: true);
            Trace.TraceWarning(ex.Message);
        }
    }
}
