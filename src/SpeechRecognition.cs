using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumenPlayer;

internal static class SpeechRecognition
{
    private const string ModelName = "ggml-base.en-q5_1.bin";
    private static readonly string Tools = Path.Combine(AppContext.BaseDirectory, "Speech");
    private static readonly string Cache = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "LumenPlayer", "Subtitles");

    public static bool IsAvailable =>
        File.Exists(Path.Combine(Tools, "Whisper", "whisper-cli.exe"))
        && File.Exists(Path.Combine(Tools, "Whisper", ModelName))
        && File.Exists(Path.Combine(Tools, "FFmpeg", "ffmpeg.exe"))
        && File.Exists(Path.Combine(Tools, "FFmpeg", "ffprobe.exe"));

    public static string GetSubtitlePath(string video)
    {
        var file = new FileInfo(video);
        var identity = $"{file.FullName.ToUpperInvariant()}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}\n{ModelName}";
        return Path.Combine(Cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".en.srt");
    }

    public static async Task Generate(string input, string target, IProgress<string> progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Cache);
        var scratch = Directory.CreateTempSubdirectory("lumen-speech-");
        var wave = Path.Combine(scratch.FullName, "audio.wav");
        var output = Path.Combine(scratch.FullName, "captions");
        var pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            progress.Report("Buscando la pista en inglés…");
            var metadata = await Run("FFmpeg", "ffprobe.exe",
                ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index:stream_tags=language:stream_disposition=default",
                 "-of", "json", input], cancellation, capture: true).ConfigureAwait(false);
            using var json = JsonDocument.Parse(metadata);
            var audio = json.RootElement.GetProperty("streams").EnumerateArray()
                .OrderByDescending(IsEnglish)
                .ThenByDescending(stream => stream.TryGetProperty("disposition", out var disposition)
                    && disposition.TryGetProperty("default", out var flag) && flag.GetInt32() == 1)
                .ToArray();
            if (audio.Length == 0)
                throw new InvalidDataException("El archivo no tiene pistas de audio.");
            var index = audio[0].GetProperty("index").GetInt32().ToString(CultureInfo.InvariantCulture);

            progress.Report("Extrayendo el audio…");
            await Run("FFmpeg", "ffmpeg.exe",
                ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", input, "-map", "0:" + index,
                 "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wave], cancellation).ConfigureAwait(false);

            progress.Report("Reconociendo el diálogo en inglés…");
            var lastPercent = -1;
            await Run("Whisper", "whisper-cli.exe",
                ["-m", Path.Combine(Tools, "Whisper", ModelName), "-f", wave, "-l", "en", "-ng",
                 "-t", Math.Clamp(Environment.ProcessorCount / 2, 1, 6).ToString(CultureInfo.InvariantCulture),
                 "-pp", "-osrt", "-of", output], cancellation, onErrorLine: line =>
                {
                    var percent = line.IndexOf('%');
                    var equals = line.LastIndexOf('=');
                    if (!line.Contains("progress", StringComparison.Ordinal) || equals < 0 || percent <= equals
                        || !int.TryParse(line.AsSpan(equals + 1, percent - equals - 1), out var value)
                        || value <= lastPercent)
                        return;
                    lastPercent = value;
                    progress.Report($"Generando subtítulos · {value}%");
                }).ConfigureAwait(false);

            cancellation.ThrowIfCancellationRequested();
            var srt = output + ".srt";
            if (!File.Exists(srt) || new FileInfo(srt).Length == 0)
                throw new InvalidDataException("No se encontró diálogo para transcribir.");
            File.Copy(srt, pending);
            File.Move(pending, target, overwrite: true);
        }
        finally
        {
            try { File.Delete(pending); scratch.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Limpieza de subtítulos: {0}", ex.Message);
            }
        }
    }

    private static bool IsEnglish(JsonElement stream) =>
        stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var language)
        && language.GetString()?.ToLowerInvariant() is "eng" or "en" or "en-us" or "en-gb";

    private static async Task<string> Run(string directory, string name, string[] arguments, CancellationToken cancellation,
        bool capture = false, Action<string>? onErrorLine = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var directoryPath = Path.Combine(Tools, directory);
        var info = new ProcessStartInfo(Path.Combine(directoryPath, name))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"No se pudo iniciar {name}.");
        using var stop = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning("Cancelación de subtítulos: {0}", ex.Message);
            }
        });
        var stdout = capture ? process.StandardOutput.ReadToEndAsync() : Task.Run(async () =>
        {
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null).ConfigureAwait(false);
            return string.Empty;
        });
        var stderr = ReadErrors();
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new IOException($"{name} terminó con código {process.ExitCode}.");
        return await stdout.ConfigureAwait(false);

        async Task ReadErrors()
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                onErrorLine?.Invoke(line);
        }
    }
}
