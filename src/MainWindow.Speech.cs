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
        string? cached = null;
        try
        {
            var candidate = SpeechRecognition.GetSubtitlePath(entry.Location);
            if (File.Exists(candidate))
                cached = candidate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(ex.Message);
        }

        if (cached is not null)
        {
            yield return Command("Cargar SRT generado (inglés)", () => AttachSubtitles([cached]));
            yield return Command("Guardar copia del SRT…", () => SaveGeneratedSubtitles(entry, cached));
        }
        else
        {
            yield return SpeechRecognition.IsAvailable
                ? Command("Generar subtítulos automáticos (SRT)", () => GenerateSubtitles(entry))
                : Command("Motor de subtítulos no disponible", () => { }, enabled: false);
        }
    }

    private async void GenerateSubtitles(QueueEntry entry)
    {
        if (_subtitleGeneration is not null)
            return;
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
            await SpeechRecognition.Generate(entry.Location, target, progress, generation.Token);
            generation.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(_current, entry))
            {
                AttachSubtitles([target]);
                if (entry.Subtitles.Contains(target))
                    SetStatus("Subtítulos en inglés listos");
            }
            else
            {
                entry.Subtitles.Add(target);
                SetStatus("Subtítulos en inglés listos para " + entry.Title);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
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
