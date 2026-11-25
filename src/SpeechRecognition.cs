using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LumenPlayer;

internal static class SpeechRecognition
{
    private const string ModelName = "ggml-small.en-q5_1.bin";
    private const string DialogueReviewModelName = "ggml-large-v3-turbo-q5_0.bin";
    private const string LegacyModelName = "ggml-base.en-q5_1.bin";
    private const string VoiceModelName = "ggml-silero-v5.1.2.bin";
    private const string CurrentSubtitleSuffix = ".lumen.v8.en.srt";
    private const string PreviousReviewSubtitleSuffix = ".lumen.v7.en.srt";
    private const string MixedSubtitleSuffix = ".lumen.v6.en.srt";
    private const string ReviewedSubtitleSuffix = ".lumen.v5.en.srt";
    private const string AlignedSubtitleSuffix = ".lumen.v4.en.srt";
    private const string PreviousSubtitleSuffix = ".lumen.v3.en.srt";
    private static readonly Regex VoiceTiming = new(@"vad_segment_info: orig_start: ([0-9.]+), orig_end: ([0-9.]+), vad_start: ([0-9.]+), vad_end: ([0-9.]+)", RegexOptions.CultureInvariant);
    private static readonly string Tools = Path.Combine(AppContext.BaseDirectory, "Speech");
    private static readonly string Cache = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "LumenPlayer", "Subtitles");

    public static bool IsAvailable =>
        File.Exists(Path.Combine(Tools, "Whisper", "whisper-cli.exe"))
        && File.Exists(Path.Combine(Tools, "Whisper", ModelName))
        && File.Exists(Path.Combine(Tools, "Whisper", VoiceModelName))
        && File.Exists(Path.Combine(Tools, "FFmpeg", "ffmpeg.exe"))
        && File.Exists(Path.Combine(Tools, "FFmpeg", "ffprobe.exe"));

    public static bool SupportsLiveCaptions => IsAvailable;

    public static string GetSubtitlePath(string video) => video + CurrentSubtitleSuffix;

    public static string? FindGeneratedSubtitle(string video)
    {
        var target = GetSubtitlePath(video);
        if (File.Exists(target))
            return target;

        target = video + PreviousReviewSubtitleSuffix;
        if (File.Exists(target))
            return target;

        target = video + MixedSubtitleSuffix;
        if (File.Exists(target))
            return target;

        target = video + ReviewedSubtitleSuffix;
        if (File.Exists(target))
            return target;

        target = video + AlignedSubtitleSuffix;
        if (File.Exists(target))
            return target;

        var previous = video + PreviousSubtitleSuffix;
        if (File.Exists(previous))
            return previous;

        target = video + ".lumen.v2.en.srt";
        if (File.Exists(target))
            return target;

        target = video + ".lumen.en.srt";
        if (File.Exists(target))
            return target;

        string? legacy = null;
        try
        {
            var file = new FileInfo(video);
            var identity = $"{file.FullName.ToUpperInvariant()}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}\n{LegacyModelName}";
            legacy = Path.Combine(Cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".en.srt");
            if (!File.Exists(legacy))
                return null;
            using var pending = new PendingSubtitle(previous);
            using var source = File.OpenRead(legacy);
            source.CopyTo(pending.Stream);
            pending.Publish();
            return previous;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(ex.Message);
            return File.Exists(previous) ? previous : File.Exists(legacy) ? legacy : null;
        }
    }

    private sealed class PendingSubtitle : IDisposable
    {
        private readonly string _target;
        private readonly string _path;
        public FileStream Stream { get; }

        public PendingSubtitle(string target)
        {
            _target = target;
            _path = Path.Combine(Path.GetDirectoryName(target)!, ".lumen-" + Guid.NewGuid().ToString("N") + ".tmp");
            Stream = File.Open(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        public void Publish()
        {
            Stream.Dispose();
            try { File.Move(_path, _target); }
            catch (IOException) when (File.Exists(_target)) { }
        }

        public void Dispose()
        {
            Stream.Dispose();
            try { File.Delete(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Trace.TraceWarning(ex.Message); }
        }
    }

    public static async Task Generate(string input, string target, int preferredOrdinal, IProgress<string> progress,
        CancellationToken cancellation)
    {
        if (File.Exists(target))
            return;
        using var pending = new PendingSubtitle(target);
        var scratch = Directory.CreateTempSubdirectory("lumen-speech-");
        var wave = Path.Combine(scratch.FullName, "audio.wav");
        var output = Path.Combine(scratch.FullName, "captions");
        try
        {
            progress.Report("Buscando la pista en inglés…");
            var audio = await FindAudioStream(input, preferredOrdinal, cancellation, live: false).ConfigureAwait(false);

            progress.Report("Extrayendo el audio…");
            await ExtractAudio(input, audio, wave, cancellation, mixDialogue: true).ConfigureAwait(false);
            var audioEnd = await ReadWaveDuration(wave, cancellation).ConfigureAwait(false);
            if (audioEnd == 0)
                throw new InvalidDataException("La pista seleccionada no contiene audio.");

            progress.Report("Reconociendo el diálogo en inglés…");
            var lastPercent = -1;
            var voiceTimes = new List<VoiceSpan>();
            var cues = await ReadCaptions(wave, output, 0, audioEnd, audio.UseCenterChannel, live: false, cancellation, line =>
                {
                    var percent = line.IndexOf('%');
                    var equals = line.LastIndexOf('=');
                    if (!line.Contains("progress", StringComparison.Ordinal) || equals < 0 || percent <= equals
                        || !int.TryParse(line.AsSpan(equals + 1, percent - equals - 1), out var value)
                        || value <= lastPercent)
                        return;
                    lastPercent = value;
                    progress.Report($"Generando subtítulos · {value}%");
                }, voiceTimes: voiceTimes).ConfigureAwait(false);

            cancellation.ThrowIfCancellationRequested();
            if (cues.Count != 0 && WordAlignment.IsAvailable)
            {
                progress.Report("Revisando el inicio de las frases…");
                await ReviewSpeechOnsets(wave, output, audioEnd, cues, voiceTimes, cancellation).ConfigureAwait(false);
                progress.Report("Alineando las palabras con el audio…");
                WordAlignment.Refine(wave, cues, 0, cancellation);
            }
            await ReviewUncertainCues(wave, audio, audioEnd, output, cues, voiceTimes, progress, cancellation)
                .ConfigureAwait(false);
            await RecoverMissingDialogue(wave, audio, audioEnd, output, cues, voiceTimes, progress, cancellation)
                .ConfigureAwait(false);
            await RecoverOpeningDialogue(input, audio, audioEnd, output, cues, progress, cancellation)
                .ConfigureAwait(false);
            if (cues.Count == 0)
                throw new InvalidDataException("No se encontró diálogo para transcribir.");
            await WriteSrt(pending.Stream, cues, audioEnd, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            pending.Publish();
        }
        finally
        {
            try { scratch.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Limpieza de subtítulos: {0}", ex.Message);
            }
        }
    }

    private static async Task RecoverOpeningDialogue(string input, AudioStream audio, long audioEnd, string output,
        List<CaptionCue> cues, IProgress<string> progress, CancellationToken cancellation)
    {
        var model = Path.Combine(Tools, "Whisper", DialogueReviewModelName);
        if (audio.Layout != "stereo" || !File.Exists(model))
            return;

        var end = Math.Min(90000, cues.Count == 0 ? audioEnd : cues.Min(cue => cue.Start));
        if (end < 12000)
            return;

        progress.Report("Buscando voces bajas al inicio…");
        var review = output + "-opening";
        var clip = review + ".wav";
        try
        {
            // The center estimate makes centered speech easier to detect over the stereo mix.
            const string filter = "dialoguenhance=original=0:enhance=3:voice=4,pan=mono|c0=FC,"
                + "highpass=f=150,dynaudnorm=f=250:g=9";
            await ExtractAudio(input, audio, clip, cancellation, duration: end, filterOverride: filter)
                .ConfigureAwait(false);
            var spans = new List<VoiceSpan>();
            await Run("Whisper", "whisper-cli.exe",
                ["-m", model, "-f", clip, "-l", "en", "-ng", "-t",
                 Math.Clamp(Environment.ProcessorCount / 2, 1, 8).ToString(CultureInfo.InvariantCulture),
                 "-ojf", "-nf", "-nfa", "-mc", "0", "-bs", "5", "-dtw", "large.v3.turbo",
                 "--vad", "-vm", Path.Combine(Tools, "Whisper", VoiceModelName),
                 "-vt", "0.22", "-vp", "350", "-vsd", "180", "-vspd", "140", "-of", review],
                cancellation, onErrorLine: line => ReadVoiceSpan(line, spans)).ConfigureAwait(false);
            if (spans.Count == 0)
                return;

            var recovered = await ReadTimedCues(review, 0, end, spans, live: false, cancellation, dtw: true)
                .ConfigureAwait(false);
            cues.AddRange(recovered.Where(cue => cue.WeakestToken >= 0.30
                && cue.WordStarts is { Length: >= 3 } && cue.End - cue.Start >= 500));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning("Revisión de voces bajas al inicio del SRT: {0}", ex.Message);
        }
    }

    private static async Task ReviewSpeechOnsets(string wave, string output, long audioEnd, List<CaptionCue> cues,
        List<VoiceSpan> voiceTimes, CancellationToken cancellation)
    {
        cues.Sort((first, second) => first.Start.CompareTo(second.Start));
        var cursor = 0;
        for (var i = 0; i < voiceTimes.Count && cursor < cues.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var voice = voiceTimes[i];
            var previousVoiceEnd = i > 0 ? voiceTimes[i - 1].OriginalEnd : 0;
            if (voice.OriginalStart - previousVoiceEnd < 1200 || voice.OriginalEnd - voice.OriginalStart < 700)
                continue;
            while (cursor < cues.Count && cues[cursor].End <= voice.OriginalStart)
                cursor++;
            if (cursor == cues.Count)
                break;
            var first = cues[cursor];
            // A phrase starting at the first retained samples may have lost its first word to VAD.
            if (Math.Abs(first.Start - voice.OriginalStart) > 280 || first.End - first.Start < 600)
                continue;
            var after = cursor + 1;
            while (after < cues.Count && cues[after].Start <= voice.OriginalEnd)
                after++;
            var last = cues[after - 1];
            if (last.End - first.Start > 4200)
                continue;

            var from = Math.Max(previousVoiceEnd, voice.OriginalStart - 2200);
            var to = Math.Min(audioEnd, Math.Min(last.End + 1800,
                i + 1 < voiceTimes.Count ? voiceTimes[i + 1].OriginalStart : audioEnd));
            if (to - from < 1500 || to < last.End)
                continue;
            var clip = output + "-onset.wav";
            var decoded = output + "-onset";
            try
            {
                await ExtractAudio(wave, new AudioStream(0, false), clip, cancellation, from, to - from).ConfigureAwait(false);
                await Transcribe(clip, decoded, false, live: true, cancellation, useVad: false, widerBeam: true).ConfigureAwait(false);
                await using var stream = File.OpenRead(decoded + ".json");
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation).ConfigureAwait(false);
                var transcript = string.Join(' ', json.RootElement.GetProperty("transcription").EnumerateArray()
                    .Select(segment => segment.GetProperty("text").GetString()?.Trim()));
                var original = NormalizeForReview(string.Join(' ', cues.GetRange(cursor, after - cursor).Select(cue => cue.Text)));
                var alternative = NormalizeForReview(transcript);
                var added = alternative.Length - original.Length;
                if (added <= 0 || !alternative.EndsWith(original, StringComparison.Ordinal))
                    continue;
                var addedWords = alternative.AsSpan(0, added).Count(' ');
                if (addedWords is < 1 or > 2)
                    continue;

                // Treat the second decode as a proposal. The added words must align to
                // actual audio before the original cue and fit between neighboring cues.
                var proposed = new List<CaptionCue> { new(Math.Max(from, first.Start - 1100), last.End, transcript.Trim(),
                    WeakestToken: first.WeakestToken) };
                WordAlignment.Refine(clip, proposed, from, cancellation, verifiedPrefix: addedWords + 1);
                var aligned = proposed[0];
                if (aligned.WordEnds is null || aligned.Start >= first.Start - 100
                    || aligned.Start < (cursor > 0 ? cues[cursor - 1].End : 0)
                    || (after < cues.Count && aligned.End > cues[after].Start))
                    continue;
                cues.RemoveRange(cursor, after - cursor);
                cues.Insert(cursor, aligned);
                cursor++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning("Revisión del inicio de subtítulos SRT: {0}", ex.Message);
            }
        }
    }

    private static async Task ReviewUncertainCues(string wave, AudioStream audio, long audioEnd, string output,
        List<CaptionCue> cues, List<VoiceSpan> voiceTimes, IProgress<string> progress, CancellationToken cancellation)
    {
        if (!cues.Any(cue => cue.WeakestToken < 0.20))
            return;

        var decoded = output + "-review";
        var lastProgress = -1;
        cues.Sort((first, second) => first.Start.CompareTo(second.Start));
        for (var index = 0; index < cues.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var uncertain = cues[index];
            if (uncertain.WeakestToken >= 0.20)
                continue;
            var percent = 100 * index / cues.Count;
            if (percent >= lastProgress + 5 || lastProgress < 0)
            {
                progress.Report($"Revisando el diálogo de baja confianza · {percent}%");
                lastProgress = percent;
            }

            // Decode the original contiguous audio around the uncertain phrase again.
            // The long VAD pass can assign a word to a separate burst of sound.
            var from = Math.Max(0, uncertain.Start - 2350);
            for (var span = 1; span < voiceTimes.Count && voiceTimes[span].OriginalStart < uncertain.End; span++)
            {
                var speech = voiceTimes[span];
                var preceding = voiceTimes[span - 1];
                if (preceding.OriginalEnd > uncertain.Start && preceding.OriginalStart <= uncertain.Start + 350
                    && speech.OriginalStart - preceding.OriginalEnd >= 1000)
                {
                    from = Math.Max(from, speech.OriginalStart - 2200);
                    break;
                }
            }
            var to = Math.Min(audioEnd, Math.Max(from + 9350, uncertain.End + 1800));
            var previousEnd = index > 0 ? cues[index - 1].End : 0;
            try
            {
                var retry = await ReadSrtReviewWindow(wave, audio, decoded, from, to, cancellation).ConfigureAwait(false);
                var replacement = retry.Where(cue => cue.Start < uncertain.End && cue.End > uncertain.Start).ToList();
                if (replacement.Count == 0 || replacement.Any(cue => cue.WeakestToken < 0.60)
                    || replacement[0].Start < previousEnd || replacement[0].Start < uncertain.Start - 350
                    || replacement[^1].End < uncertain.End - 350)
                    continue;

                // A decoded sentence may extend into a fragment of the same sentence.
                // Keep neighboring dialogue unless the replacement already contains it.
                var after = index + 1;
                var normalized = NormalizeForReview(string.Join(' ', replacement.Select(cue => cue.Text)));
                while (after < cues.Count && cues[after].Start < replacement[^1].End
                    && cues[after].End <= replacement[^1].End + 350
                    && normalized.Contains(NormalizeForReview(cues[after].Text), StringComparison.Ordinal))
                    after++;
                if (after < cues.Count && cues[after].Start < replacement[^1].End
                    || replacement[^1].End > cues[after - 1].End + 350)
                    continue;

                cues.RemoveRange(index, after - index);
                cues.InsertRange(index, replacement);
                index += replacement.Count - 1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning("Segunda pasada de subtítulos SRT: {0}", ex.Message);
            }
        }
    }

    private static string NormalizeForReview(string value) =>
        " " + string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)).ToLowerInvariant())) + " ";

    private static async Task RecoverMissingDialogue(string wave, AudioStream audio, long audioEnd, string output,
        List<CaptionCue> cues, List<VoiceSpan> voiceTimes, IProgress<string> progress, CancellationToken cancellation)
    {
        cues.Sort((first, second) => first.Start.CompareTo(second.Start));
        var nextCue = 0;
        var reviewedUntil = 0L;
        var lastProgress = -1;
        for (var index = 0; index < voiceTimes.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var speech = voiceTimes[index];
            if (speech.OriginalEnd - speech.OriginalStart < 500 || speech.OriginalStart < reviewedUntil)
                continue;

            var midpoint = (speech.OriginalStart + speech.OriginalEnd) / 2;
            while (nextCue < cues.Count && cues[nextCue].End <= midpoint)
                nextCue++;
            if (nextCue < cues.Count && cues[nextCue].Start <= midpoint)
                continue;

            var percent = 100 * index / voiceTimes.Count;
            if (percent >= lastProgress + 5 || lastProgress < 0)
            {
                progress.Report($"Buscando diálogo omitido · {percent}%");
                lastProgress = percent;
            }
            var from = Math.Max(0, speech.OriginalStart - 2200);
            var to = Math.Min(audioEnd, from + 9350);
            // Leave a little overlap so a phrase at the edge can be reviewed in the next window.
            reviewedUntil = to == audioEnd ? to : to - 1600;
            try
            {
                var recovered = await ReadSrtReviewWindow(wave, audio, output + "-recover", from, to, cancellation)
                    .ConfigureAwait(false);
                foreach (var cue in recovered)
                {
                    if (cue.WeakestToken < 0.65 || cue.Text.Count(char.IsWhiteSpace) < 2 || cue.Start >= reviewedUntil)
                        continue;
                    var voice = voiceTimes.FirstOrDefault(span => span.OriginalEnd > cue.Start
                        && span.OriginalStart <= cue.Start + 700 && span.OriginalStart < cue.End - 300);
                    if (voice is null)
                        continue;
                    var start = Math.Max(cue.Start, voice.OriginalStart - 120);
                    var after = cues.FindIndex(existing => existing.Start >= start);
                    if (after < 0)
                        after = cues.Count;
                    if ((after > 0 && cues[after - 1].End > start)
                        || (after < cues.Count && cues[after].Start < cue.End))
                        continue;
                    cues.Insert(after, cue with { Start = start });
                    nextCue = Math.Min(nextCue, after);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning("Revisión de diálogo omitido: {0}", ex.Message);
            }
        }
    }

    private static async Task<List<CaptionCue>> ReadSrtReviewWindow(string wave, AudioStream audio, string output,
        long from, long to, CancellationToken cancellation)
    {
        var clip = output + ".wav";
        if (to <= from)
            return [];
        await ExtractAudio(wave, new AudioStream(0, false), clip, cancellation, from, to - from).ConfigureAwait(false);
        var cues = await ReadCaptions(clip, output, from, to, audio.UseCenterChannel, live: true, cancellation,
            widerBeam: true).ConfigureAwait(false);
        WordAlignment.Refine(clip, cues, from, cancellation, live: true);
        return cues;
    }

    private static async Task WriteSrt(Stream stream, List<CaptionCue> cues, long audioEnd, CancellationToken cancellation)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        var number = 0;
        foreach (var cue in SrtLayout.Create(cues, audioEnd))
        {
            cancellation.ThrowIfCancellationRequested();
            var block = $"{++number}\n{SrtTime(cue.Start)} --> {SrtTime(cue.End)}\n{cue.Text}\n\n";
            await writer.WriteAsync(block.AsMemory(), cancellation).ConfigureAwait(false);
        }
        if (number == 0)
            throw new InvalidDataException("No se encontró diálogo para transcribir.");
    }

    private static string SrtTime(long offset)
    {
        var time = TimeSpan.FromMilliseconds(offset);
        return FormattableString.Invariant($"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }

    internal static async Task<SrtTimeline?> ReadEmbeddedSrt(string input, int ordinal, int trackCount,
        CancellationToken cancellation)
    {
        var metadata = await Run("FFmpeg", "ffprobe.exe",
            ["-v", "error", "-select_streams", "s", "-show_entries", "stream=index,codec_name",
             "-of", "json", input], cancellation, capture: true).ConfigureAwait(false);
        using var json = JsonDocument.Parse(metadata);
        if (!json.RootElement.TryGetProperty("streams", out var tracks) || tracks.GetArrayLength() != trackCount
            || !tracks[ordinal].TryGetProperty("codec_name", out var codec) || codec.GetString() != "subrip")
            return null;

        var index = tracks[ordinal].GetProperty("index").GetInt32().ToString(CultureInfo.InvariantCulture);
        var text = await Run("FFmpeg", "ffmpeg.exe",
            ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", input, "-map", "0:" + index,
             "-c:s", "srt", "-f", "srt", "pipe:1"], cancellation, capture: true).ConfigureAwait(false);
        return SrtTimeline.Parse(text);
    }

    internal sealed record AudioStream(int Index, bool UseCenterChannel, string? Layout = null);

    public static async Task<AudioStream> FindAudioStream(string input, int preferredOrdinal, CancellationToken cancellation,
        bool live = true)
    {
        var metadata = await Run("FFmpeg", "ffprobe.exe",
            ["-v", "error", "-select_streams", "a", "-show_entries",
             "stream=index,channel_layout:stream_tags=language,title:stream_disposition=default,comment,visual_impaired",
             "-of", "json", input], cancellation, capture: true).ConfigureAwait(false);
        using var json = JsonDocument.Parse(metadata);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Length == 0)
            throw new InvalidDataException("El archivo no tiene pistas de audio.");
        var selected = preferredOrdinal >= 0 && preferredOrdinal < streams.Length ? streams[preferredOrdinal] : default;
        var stream = live && selected.ValueKind != JsonValueKind.Undefined ? selected
            : streams.Where(candidate => EnglishPriority(candidate) > 0)
                .OrderBy(candidate => !live && IsSupplementalAudio(candidate))
                .ThenByDescending(EnglishPriority)
                .ThenByDescending(candidate => candidate.Equals(selected))
                .ThenByDescending(candidate => candidate.TryGetProperty("disposition", out var disposition)
                    && disposition.TryGetProperty("default", out var flag) && flag.GetInt32() == 1)
                .FirstOrDefault();
        if (stream.ValueKind == JsonValueKind.Undefined || EnglishPriority(stream) == 0)
            throw new InvalidDataException(live ? "Selecciona una pista de audio en inglés para los CC."
                : "No se encontró una pista de audio en inglés.");
        var layout = stream.TryGetProperty("channel_layout", out var value) ? value.GetString() : null;
        return new AudioStream(stream.GetProperty("index").GetInt32(), layout is "5.1" or "5.1(side)", layout);
    }

    private static bool IsSupplementalAudio(JsonElement stream)
    {
        if (stream.TryGetProperty("disposition", out var disposition)
            && ((disposition.TryGetProperty("comment", out var comment) && comment.GetInt32() == 1)
                || (disposition.TryGetProperty("visual_impaired", out var described) && described.GetInt32() == 1)))
            return true;
        return stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("title", out var title)
            && title.GetString() is { } name && Regex.IsMatch(name, @"\b(?:commentary|audio description|descriptive audio)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static async Task<List<CaptionCue>> RecognizeWindow(string input, AudioStream audio, long start, long end,
        long? resumeAt, CancellationToken cancellation)
    {
        var scratch = Directory.CreateTempSubdirectory("lumen-live-");
        try
        {
            var cues = await Decode(start, "first", widerBeam: false).ConfigureAwait(false);
            if (start > 0 && resumeAt is long point
                && CueAt(cues, point) is { WeakestToken: < 0.40, ReplacedSpelling: null } uncertain)
            {
                // At the first words of a weak cue, less preceding audio can help
                // Whisper keep the new phrase separate from the preceding dialogue.
                if (!audio.UseCenterChannel && uncertain.WordStarts is { Length: >= 3 } && point - uncertain.Start <= 700
                    && point - start >= 1500 && WordAlignment.IsAvailable)
                {
                    var shorter = await TryDecode(start + 250, "shorter", widerBeam: false).ConfigureAwait(false);
                    if (shorter is not null && HasConfirmedSpelling(shorter, uncertain, point))
                        return shorter;

                    // An earlier cut can restore the preceding word that the acoustic check needs.
                    var earlier = await TryDecode(Math.Max(0, start - 500), "earlier", widerBeam: false).ConfigureAwait(false);
                    if (earlier is not null && HasConfirmedSpelling(earlier, uncertain, point))
                        return earlier;
                }

                // A seek can also leave a weak guess at the edge of Whisper's context.
                var retry = await TryDecode(Math.Max(0, start - 350), "retry", widerBeam: true).ConfigureAwait(false);
                if (retry is not null && CueAt(retry, point) is { WeakestToken: >= 0.60 } replacement
                    && Math.Abs(replacement.Start - uncertain.Start) <= 350)
                    return retry;
            }
            return cues;
        }
        finally
        {
            try { scratch.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Limpieza de subtítulos en vivo: {0}", ex.Message);
            }
        }

        async Task<List<CaptionCue>?> TryDecode(long from, string name, bool widerBeam)
        {
            try
            {
                return await Decode(from, name, widerBeam).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or System.ComponentModel.Win32Exception)
            {
                Trace.TraceWarning("Segunda pasada de subtítulos en vivo: {0}", ex.Message);
                return null;
            }
        }

        async Task<List<CaptionCue>> Decode(long from, string name, bool widerBeam)
        {
            var output = Path.Combine(scratch.FullName, name);
            var wave = output + ".wav";
            await ExtractAudio(input, audio, wave, cancellation, from, end - from).ConfigureAwait(false);
            // An audio stream may end before the video. FFmpeg then writes only the WAV header.
            if (new FileInfo(wave).Length < 1600)
                return [];
            var cues = await ReadCaptions(wave, output, from, end, audio.UseCenterChannel, live: true,
                cancellation, widerBeam: widerBeam).ConfigureAwait(false);
            if (!audio.UseCenterChannel)
            {
                // Isolated, very weak guesses on short effects sometimes survive VAD.
                cues.RemoveAll(cue => cue.WeakestToken < 0.12 && cue.End - cue.Start < 900
                    && !cue.Text.Any(char.IsWhiteSpace)
                    && !cues.Any(other => other != cue && other.WeakestToken >= 0.40
                        && other.Start < cue.End + 350 && other.End > cue.Start - 350));
            }
            WordAlignment.Refine(wave, cues, from, cancellation, live: true, reviewSpelling: !audio.UseCenterChannel);
            return cues;
        }

        static CaptionCue? CueAt(List<CaptionCue> cues, long position) =>
            cues.LastOrDefault(cue => cue.Start <= position && cue.End > position);

        static bool HasConfirmedSpelling(List<CaptionCue> candidate, CaptionCue original, long point) =>
            CueAt(candidate, point) is { ReplacedSpelling: { } spelling } supported
            && Math.Abs(supported.Start - original.Start) <= 350
            && original.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word =>
                LiveCaptionTimeline.NormalizeWord(word) == spelling);
    }

    private static async Task<List<CaptionCue>> ReadCaptions(string wave, string output, long start, long end,
        bool useCenterChannel, bool live, CancellationToken cancellation, Action<string>? progress = null,
        bool widerBeam = false, List<VoiceSpan>? voiceTimes = null)
    {
        voiceTimes ??= [];
        await Transcribe(wave, output, useCenterChannel, live, cancellation, line =>
        {
            ReadVoiceSpan(line, voiceTimes);
            progress?.Invoke(line);
        }, widerBeam: widerBeam).ConfigureAwait(false);
        if (voiceTimes.Count == 0)
            return [];

        // Long pauses removed by VAD let Whisper attach later words to an earlier voice.
        // For live captions, recognize each side of those pauses on its original audio.
        const int splitPauseMs = 1000;
        if (live && voiceTimes.Zip(voiceTimes.Skip(1)).Any(pair =>
            pair.Second.OriginalStart - pair.First.OriginalEnd >= splitPauseMs))
        {
            var cues = new List<CaptionCue>();
            var groupStart = voiceTimes[0].OriginalStart;
            for (var i = 0; i < voiceTimes.Count; i++)
            {
                var groupEnd = voiceTimes[i].OriginalEnd;
                if (i + 1 < voiceTimes.Count && voiceTimes[i + 1].OriginalStart - groupEnd < splitPauseMs)
                    continue;

                var clipStart = Math.Max(0, groupStart - 300);
                var from = Math.Min(start + clipStart, end);
                var paddedEnd = i + 1 < voiceTimes.Count ? Math.Min(groupEnd + 180, voiceTimes[i + 1].OriginalStart) : groupEnd + 180;
                var to = Math.Min(start + paddedEnd, end);
                if (to > from)
                {
                    var clipOutput = output + "-" + i.ToString(CultureInfo.InvariantCulture);
                    var clip = clipOutput + ".wav";
                    await ExtractAudio(wave, new AudioStream(0, false), clip, cancellation, clipStart, to - from)
                        .ConfigureAwait(false);
                    await Transcribe(clip, clipOutput, useCenterChannel, live, cancellation, useVad: false)
                        .ConfigureAwait(false);
                    cues.AddRange(await ReadTimedCues(clipOutput, from, to, [], live, cancellation).ConfigureAwait(false));
                }
                if (i + 1 < voiceTimes.Count)
                    groupStart = voiceTimes[i + 1].OriginalStart;
            }
            return cues;
        }

        return await ReadTimedCues(output, start, end, voiceTimes, live, cancellation).ConfigureAwait(false);
    }

    private static async Task<List<CaptionCue>> ReadTimedCues(string output, long start, long end,
        List<VoiceSpan> voiceTimes, bool live, CancellationToken cancellation, bool dtw = false)
    {
        await using var jsonFile = File.OpenRead(output + ".json");
        using var json = await JsonDocument.ParseAsync(jsonFile, cancellationToken: cancellation).ConfigureAwait(false);
        var cues = new List<CaptionCue>();
        foreach (var segment in json.RootElement.GetProperty("transcription").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var offsets = segment.GetProperty("offsets");
            var from = Math.Clamp(start + offsets.GetProperty("from").GetInt64(), start, end);
            var to = Math.Clamp(start + offsets.GetProperty("to").GetInt64(), from, end);
            var text = segment.GetProperty("text").GetString()?.Trim();
            if (to > from && text is not null && text.Any(char.IsLetterOrDigit)
                && (dtw ? text[0] is not ('[' or '(') && HasConfidentSpeech(segment, 0.70)
                    : voiceTimes.Count != 0 || end - start > 1200 || HasConfidentSpeech(segment)))
                AppendPhrases(cues, segment, text, voiceTimes, start, from, to, live, dtw);
        }
        return cues;
    }

    private static bool HasConfidentSpeech(JsonElement segment, double minimum = 0.4)
    {
        // A short isolated burst can be an effect. Require lexical evidence from the decoder.
        var scores = segment.GetProperty("tokens").EnumerateArray()
            .Where(token => token.GetProperty("id").GetInt32() < 50256
                && token.GetProperty("text").GetString()?.Any(char.IsLetterOrDigit) == true)
            .Select(token => Math.Log(Math.Max(0.000001, token.GetProperty("p").GetDouble())));
        return scores.DefaultIfEmpty(double.NegativeInfinity).Average() >= Math.Log(minimum);
    }

    private static void ReadVoiceSpan(string line, List<VoiceSpan> spans)
    {
        // The bundled Whisper CLI reports original-time offsets for segments and compressed-time offsets for tokens.
        var match = VoiceTiming.Match(line);
        if (match.Success)
            spans.Add(new(Read(1), Read(2), Read(3), Read(4)));

        long Read(int group) => (long)Math.Round(double.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture) * 1000);
    }

    private static void AppendPhrases(List<CaptionCue> cues, JsonElement segment, string text, List<VoiceSpan> voiceTimes,
        long windowStart, long from, long to, bool live, bool dtw)
    {
        var words = new List<CaptionCue>();
        var tokens = segment.GetProperty("tokens").EnumerateArray()
            .Where(token => token.GetProperty("id").GetInt32() < 50256).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var part = token.GetProperty("text").GetString() ?? "";
            if (part.Length == 0)
                continue;
            var offsets = token.GetProperty("offsets");
            var lexical = part.Any(char.IsLetterOrDigit);
            // Whisper reports DTW anchors in 10 ms units on the VAD-compressed audio.
            var begin = dtw ? DtwTime(token) : offsets.GetProperty("from").GetInt64();
            var finish = !dtw ? offsets.GetProperty("to").GetInt64()
                : !lexical ? begin + 10
                : i + 1 < tokens.Length ? Math.Clamp(DtwTime(tokens[i + 1]), begin + 10, begin + 650)
                : begin + 180;
            var timed = begin >= 0 && finish > begin;
            if (dtw && !timed)
                return;
            var probability = lexical ? token.GetProperty("p").GetDouble() : 1;
            if (!char.IsWhiteSpace(part[0]) && words.Count > 0)
            {
                var word = words[^1];
                words[^1] = word with
                {
                    Text = word.Text + part,
                    End = timed ? Math.Max(word.End, OriginalTime(finish, ending: true)) : word.End,
                    WeakestToken = Math.Min(word.WeakestToken, probability)
                };
            }
            else if (!string.IsNullOrWhiteSpace(part))
            {
                var beginAt = timed ? OriginalTime(begin) : words.LastOrDefault()?.End ?? from;
                words.Add(new(beginAt, timed ? Math.Max(beginAt, OriginalTime(finish, ending: true)) : beginAt,
                    part.Trim(), WeakestToken: probability));
            }
        }

        if (words.Count == 0 || words[^1].End - words[0].Start < 150)
        {
            cues.Add(new(from, to, text, WeakestToken: words.Count == 0 ? 1 : words.Min(word => word.WeakestToken)));
            return;
        }

        var phrase = new StringBuilder();
        var wordStarts = new List<long>();
        var previousWord = "";
        var phraseStart = words[0].Start;
        var phraseEnd = phraseStart;
        var weakestToken = 1d;
        foreach (var word in words)
        {
            if (phrase.Length > 0 && (word.Start - phraseEnd > (live ? 350 : 1250) || word.Start - phraseStart >= 5000
                || phrase.Length + 1 + word.Text.Length > 72
                || (live
                    ? LiveCaptionTimeline.IsPhraseBreak(previousWord, wordStarts.Count, word.Start - wordStarts[^1])
                    : phrase[^1] is '.' or '?' or '!' && phrase.Length >= 16)))
            {
                AddPhrase();
                phrase.Clear();
                wordStarts.Clear();
                phraseStart = word.Start;
                phraseEnd = phraseStart;
                weakestToken = 1;
            }
            if (phrase.Length > 0)
                phrase.Append(' ');
            phrase.Append(word.Text);
            wordStarts.Add(word.Start);
            previousWord = word.Text;
            phraseEnd = Math.Max(phraseEnd, word.End);
            weakestToken = Math.Min(weakestToken, word.WeakestToken);
        }
        AddPhrase();

        static long DtwTime(JsonElement token) => token.GetProperty("t_dtw").GetInt64() * 10;

        void AddPhrase()
        {
            var end = Math.Min(to, Math.Max(phraseEnd, phraseStart + 160));
            if (end > phraseStart)
                cues.Add(new(phraseStart, end, phrase.ToString(), wordStarts.ToArray(), weakestToken));
        }

        long OriginalTime(long offset, bool ending = false)
        {
            if (voiceTimes.Count == 0)
                return Math.Clamp(windowStart + offset, from, to);

            // Find the preceding voice span in the compressed timeline. Padding between spans
            // belongs to the preceding word's end or the following word's start, respectively.
            var left = 0;
            var right = voiceTimes.Count;
            while (left < right)
            {
                var middle = left + (right - left) / 2;
                if (voiceTimes[middle].CompressedStart <= offset)
                    left = middle + 1;
                else
                    right = middle;
            }
            var span = voiceTimes[Math.Max(0, left - 1)];
            var original = !ending && offset > span.CompressedEnd && left < voiceTimes.Count
                ? voiceTimes[left].OriginalStart
                : span.OriginalStart + Math.Clamp(offset - span.CompressedStart, 0, span.OriginalEnd - span.OriginalStart);
            return Math.Clamp(windowStart + original, from, to);
        }
    }

    private sealed record VoiceSpan(long OriginalStart, long OriginalEnd, long CompressedStart, long CompressedEnd);

    private static Task<string> ExtractAudio(string input, AudioStream audio, string wave, CancellationToken cancellation,
        long start = 0, long? duration = null, bool mixDialogue = false, string? filterOverride = null)
    {
        var resample = mixDialogue ? "aresample=16000:async=1:min_hard_comp=0.025:first_pts=0"
            : "aresample=16000:async=1:first_pts=0";
        // Keep off-center speech audible, favor the dialogue channel and leave out LFE.
        const string front = "pan=mono|c0=0.52*FC+0.20*FL+0.20*FR";
        var mix = mixDialogue ? audio.Layout switch
        {
            "3.0" => front,
            "5.0" or "5.1" => front + "+0.04*BL+0.04*BR",
            "5.0(side)" or "5.1(side)" => front + "+0.04*SL+0.04*SR",
            "7.1" => front + "+0.02*BL+0.02*BR+0.02*SL+0.02*SR",
            _ => null
        } : audio.UseCenterChannel ? "pan=mono|c0=FC" : null;
        var filter = filterOverride is not null ? filterOverride + "," + resample
            : mix is null ? resample : mix + "," + resample;
        return Run("FFmpeg", "ffmpeg.exe",
            ["-hide_banner", "-nostdin", "-loglevel", "error", "-y",
             ..(start > 0 ? new[] { "-ss", Seconds(start) } : Array.Empty<string>()),
             "-i", input, "-map", "0:" + audio.Index.ToString(CultureInfo.InvariantCulture),
             ..(duration is long length ? new[] { "-t", Seconds(length) } : Array.Empty<string>()),
             "-af", filter,
             "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wave], cancellation);
    }

    private static async Task<long> ReadWaveDuration(string wave, CancellationToken cancellation)
    {
        var value = await Run("FFmpeg", "ffprobe.exe",
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=duration_ts",
             "-of", "default=nw=1:nk=1", wave], cancellation, capture: true).ConfigureAwait(false);
        // The extracted PCM WAV has a time base of 1/16000 seconds.
        return long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var samples)
            ? samples / 16 : 0;
    }

    private static string Seconds(long milliseconds) => (milliseconds / 1000d).ToString("0.000", CultureInfo.InvariantCulture);

    private static Task<string> Transcribe(string wave, string output, bool useCenterChannel, bool live,
        CancellationToken cancellation, Action<string>? onErrorLine = null, bool useVad = true, bool widerBeam = false) =>
        Run("Whisper", "whisper-cli.exe",
            ["-m", Path.Combine(Tools, "Whisper", ModelName), "-f", wave, "-l", "en", "-ng",
             "-t", Math.Clamp(Environment.ProcessorCount / 2, 1, live ? 6 : 8).ToString(CultureInfo.InvariantCulture),
             "-ojf", "-ml", "1000", "-sow", "-sns",
             ..(useVad ? new[] { "--vad", "-vm", Path.Combine(Tools, "Whisper", VoiceModelName),
                 "-vt", live ? (useCenterChannel ? "0.30" : "0.35") : "0.22",
                 "-vp", live ? "120" : "350", "-vsd", live ? "180" : "120", "-vspd", live ? "180" : "140" }
                 : Array.Empty<string>()),
             // Live windows contain at most 12 seconds of audio. 704 context frames cover 14.08 seconds.
             ..(live ? new[] { "-ac", "704", "-bs", useVad && !widerBeam ? "3" : "5", "-nf" }
                 : new[] { "-pp", "-bs", "5", "-mc", "64" }),
             "-of", output], cancellation, onErrorLine: onErrorLine);

    internal sealed record CaptionCue(long Start, long End, string Text, long[]? WordStarts = null,
        double WeakestToken = 1, string? ReplacedSpelling = null, long[]? WordEnds = null);

    private static int EnglishPriority(JsonElement stream)
    {
        var language = stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var tag)
            ? tag.GetString()?.Trim().ToLowerInvariant() : null;
        return language is "en" or "eng" || language?.StartsWith("en-", StringComparison.Ordinal) == true ? 2
            : language is null or "" or "und" or "mul" or "mis" or "unk" ? 1 : 0;
    }

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
