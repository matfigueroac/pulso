using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LumenPlayer;

internal static class WordAlignment
{
    private const int SamplesPerMillisecond = 16;
    private const int Padding = 450;
    private const int EdgeMargin = 100;
    private const int MaxCueLength = 6000;
    private const int MaxOnsetAdjustment = 1000;
    private const int VocabularySize = 32;
    // Wav2Vec2 Base 960h: blank is 0, word separator is 4.
    private const string LetterIds = "ETAONIHSRDLUMWCFGYPBVK'XJQZ";
    private static readonly (int Before, int After)[] SpellingContexts =
        [(680, 880), (580, 840), (915, 710), (800, 730), (520, 500), (680, 500), (180, 640), (810, 740), (820, 710)];
    private static readonly string ModelPath = Path.Combine(AppContext.BaseDirectory, "Speech", "Alignment",
        "wav2vec2-base-960h-int8.onnx");
    private static readonly Lazy<InferenceSession> Model = new(() =>
    {
        using var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) };
        return new InferenceSession(ModelPath, options);
    });

    public static bool IsAvailable => File.Exists(ModelPath);

    internal static void Refine(string path, List<SpeechRecognition.CaptionCue> cues, long origin,
        CancellationToken cancellation, bool live = false, bool reviewSpelling = false, int verifiedPrefix = 0)
    {
        if (cues.Count == 0 || !IsAvailable)
            return;

        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (!ReadPcmHeader(reader, out var dataStart, out var samples))
                return;
            var audioEnd = origin + samples / SamplesPerMillisecond;

            for (var i = 0; i < cues.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var cue = cues[i];
                if (!live && cue.WordEnds is not null)
                    continue;
                var previousEnd = i > 0 ? cues[i - 1].End : origin;
                var nextStart = i + 1 < cues.Count ? cues[i + 1].Start : audioEnd;
                var candidate = Align(reader, dataStart, origin, audioEnd, cue, live, Padding, verifiedPrefix,
                    allowLeadingSpeech: live);
                if (candidate is null && !live)
                    candidate = Align(reader, dataStart, origin, audioEnd, cue, live, 1000, verifiedPrefix,
                        allowLeadingSpeech: true);
                cancellation.ThrowIfCancellationRequested();
                // An earlier onset immediately after another phrase can be a match to its last sound.
                if (candidate is not null && candidate.Start >= previousEnd && candidate.End <= nextStart
                    && (i == 0 || candidate.Start >= cue.Start || candidate.Start - previousEnd >= EdgeMargin))
                    cue = candidate;
                cues[i] = reviewSpelling && cue.WeakestToken < 0.40 && cue.End - cue.Start < 2800
                    ? ReviewSpelling(reader, dataStart, origin, audioEnd, cue, cancellation) : cue;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OnnxRuntimeException
            or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            Trace.TraceWarning("Alineación de subtítulos: {0}", ex.Message);
        }
    }

    private static SpeechRecognition.CaptionCue? Align(BinaryReader reader, long dataStart, long origin, long audioEnd,
        SpeechRecognition.CaptionCue cue, bool live, int padding, int verifiedPrefix, bool allowLeadingSpeech)
    {
        if (cue.End <= cue.Start || cue.End - cue.Start > MaxCueLength)
            return null;
        var from = Math.Max(origin, cue.Start - padding);
        var to = Math.Min(audioEnd, cue.End + padding);
        if (to - from < 100)
            return null;
        var words = cue.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<int>();
        var wordRanges = new List<(int Start, int End)>();
        foreach (var word in words)
        {
            if (tokens.Count != 0)
                tokens.Add(4);
            var start = tokens.Count;
            foreach (var character in word)
            {
                var letter = character == '’' ? '\'' : char.ToUpperInvariant(character);
                var id = LetterIds.IndexOf(letter);
                if (live && letter == '\'')
                    continue;
                if (id >= 0)
                    tokens.Add(id + 5);
                else if (char.IsLetterOrDigit(letter))
                    return null;
            }
            if (start == tokens.Count)
                return null;
            wordRanges.Add((start, tokens.Count));
        }
        if (tokens.Count == 0)
            return null;

        var input = ReadInput(reader, dataStart, origin, from, to);
        if (input is null)
            return null;
        using var outputs = Model.Value.Run([input]);
        if (outputs.First().AsTensor<float>() is not DenseTensor<float> logits || logits.Rank != 3
            || logits.Dimensions[0] != 1 || logits.Dimensions[2] != VocabularySize)
            return null;
        var frames = logits.Dimensions[1];
        if (frames <= tokens.Count)
            return null;
        var characters = FindCharacterTimes(logits.Buffer.Span, frames, tokens, allowLeadingSpeech);
        if (characters is null)
            return null;

        var wordStarts = new long[words.Length];
        var wordEnds = new long[words.Length];
        var confidence = 0d;
        var reliableWords = 0;
        var reliableOnset = false;
        var prefixMatches = 0;
        var millisecondsPerFrame = (to - from) / (double)frames;
        for (var i = 0; i < wordRanges.Count; i++)
        {
            var (first, afterLast) = wordRanges[i];
            var score = 0d;
            for (var j = first; j < afterLast; j++)
                score += characters[j].Confidence;
            score /= afterLast - first;
            if (score >= 0.35)
            {
                reliableWords++;
                if (i < verifiedPrefix)
                    prefixMatches++;
                if (i == 0)
                    reliableOnset = true;
            }
            confidence += score;
            wordStarts[i] = AtFrame(characters[first].Start);
            var last = characters[afterLast - 1];
            // CTC can assign trailing blank frames to the last letter. Exclude that
            // silence when measuring the gap to the next word.
            wordEnds[i] = AtFrame(Math.Min(last.End, last.Start + 3));
        }
        var startTime = wordStarts[0];
        var endTime = AtFrame(characters[^1].End);
        var onsetAdjustment = !live && cue.Start <= startTime && endTime <= cue.End + 450 ? 3000 : MaxOnsetAdjustment;
        if (Math.Abs(startTime - cue.Start) > onsetAdjustment || startTime - origin < EdgeMargin
            || audioEnd - endTime < EdgeMargin
            || endTime - startTime < 150)
            return null;
        var meanConfidence = confidence / words.Length;
        var prefixConfirmed = verifiedPrefix > 0 && prefixMatches == verifiedPrefix
            && reliableWords * 4 >= words.Length * 3 && meanConfidence >= 0.52;
        if ((reliableWords == words.Length && meanConfidence >= 0.5) || prefixConfirmed)
            return cue with { Start = startTime, End = endTime, WordStarts = wordStarts, WordEnds = live ? null : wordEnds };

        // In live mode a reduced word (for example “of”) can have weak letter matches.
        // A supported later onset can remove an early flash without shortening the reading time.
        if (live && reliableOnset && reliableWords >= 3 && meanConfidence >= 0.33 && startTime > cue.Start
            && wordStarts[^1] < cue.End && cue.End - startTime >= 150)
            return cue with { Start = startTime, WordStarts = wordStarts };
        return null;

        long AtFrame(int frame) => from + (long)Math.Round(frame * millisecondsPerFrame);
    }

    private static SpeechRecognition.CaptionCue ReviewSpelling(BinaryReader reader, long dataStart, long origin,
        long audioEnd, SpeechRecognition.CaptionCue cue, CancellationToken cancellation)
    {
        var words = cue.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || cue.WordStarts is not { } times || times.Length != words.Length)
            return cue;
        var normalized = words.Select(word => word.Trim('.', ',', '!', '?', ';', ':', '"')).ToArray();
        // Changing where the window starts can hide a consonant in one crop.
        // Require two matching crops with one strong reading, or three with two clear readings.
        var matches = new List<SpellingMatch>();
        var cuts = new HashSet<(long Start, long End)>();
        foreach (var (before, after) in SpellingContexts)
        {
            cancellation.ThrowIfCancellationRequested();
            var from = Math.Max(origin, cue.Start - before);
            var to = Math.Min(audioEnd, cue.End + after);
            if (!cuts.Add((from, to)) || FindSpelling(from, to) is not { } match)
                continue;
            var agreeing = matches.Where(found => found.Index == match.Index && found.Spelling == match.Spelling
                && Math.Abs(found.Start - match.Start) <= 80).ToArray();
            if ((agreeing.Length > 0 && Math.Max(match.Confidence, agreeing.Max(found => found.Confidence)) >= 0.80f)
                || (agreeing.Length >= 2 && agreeing.Count(found => found.Confidence >= 0.60f)
                    + (match.Confidence >= 0.60f ? 1 : 0) >= 2))
            {
                var (index, spelling, _, _) = match;
                var offset = words[index].IndexOf(normalized[index], StringComparison.Ordinal);
                words[index] = words[index].Remove(offset, normalized[index].Length).Insert(offset, spelling.ToLowerInvariant());
                return cue with { Text = string.Join(' ', words), ReplacedSpelling = normalized[index] };
            }
            matches.Add(match);
        }
        return cue;

        SpellingMatch? FindSpelling(long from, long to)
        {
            var recognized = ReadSpelledWords(reader, dataStart, origin, from, to);
            for (var i = 1; i < words.Length; i++)
            {
                var word = normalized[i];
                if (word.Length < 4 || word.Any(letter => letter is < 'a' or > 'z'))
                    continue;
                var upper = word.ToUpperInvariant();
                var previous = normalized[i - 1].ToUpperInvariant();
                for (var j = 1; j < recognized.Count; j++)
                {
                    var candidate = recognized[j];
                    if (recognized[j - 1].Text != previous || Math.Abs(candidate.Start - times[i]) > 350
                        || candidate.Text.Length != upper.Length + 1)
                        continue;
                    // Require a stable preceding word and a likely missing letter inside the word.
                    for (var letter = 2; letter <= upper.Length - 2; letter++)
                        if (candidate.Confidence[letter] >= 0.52f && candidate.Text.Remove(letter, 1) == upper)
                            return new(i, candidate.Text, candidate.Start, candidate.Confidence[letter]);
                }
            }
            return null;
        }
    }

    private readonly record struct SpellingMatch(int Index, string Spelling, long Start, float Confidence);

    private static List<SpelledWord> ReadSpelledWords(BinaryReader reader, long dataStart, long origin, long from, long to)
    {
        var result = new List<SpelledWord>();
        var input = ReadInput(reader, dataStart, origin, from, to);
        if (input is null)
            return result;
        using var outputs = Model.Value.Run([input]);
        if (outputs.First().AsTensor<float>() is not DenseTensor<float> logits || logits.Rank != 3
            || logits.Dimensions[0] != 1 || logits.Dimensions[2] != VocabularySize)
            return result;

        var frames = logits.Dimensions[1];
        var previous = 0;
        var text = new StringBuilder();
        var confidence = new List<float>();
        var start = from;
        for (var frame = 0; frame < frames; frame++)
        {
            var scores = logits.Buffer.Span.Slice(frame * VocabularySize, VocabularySize);
            var label = 0;
            for (var i = 1; i < scores.Length; i++)
                if (scores[i] > scores[label])
                    label = i;
            if (label == 4 && text.Length > 0)
            {
                result.Add(new(text.ToString(), start, confidence.ToArray()));
                text.Clear();
                confidence.Clear();
            }
            if (label >= 5)
            {
                var total = 0f;
                foreach (var score in scores)
                    total += MathF.Exp(score - scores[label]);
                if (label == previous)
                    confidence[^1] = Math.Max(confidence[^1], 1 / total);
                else
                {
                    if (text.Length == 0)
                        start = from + frame * (to - from) / frames;
                    text.Append(LetterIds[label - 5]);
                    confidence.Add(1 / total);
                }
            }
            previous = label;
        }
        return result;
    }

    private static NamedOnnxValue? ReadInput(BinaryReader reader, long dataStart, long origin, long from, long to)
    {
        if (to - from < 100)
            return null;
        var sampleCount = (int)(to - from) * SamplesPerMillisecond;
        var bytes = new byte[sampleCount * sizeof(short)];
        reader.BaseStream.Position = dataStart + (from - origin) * SamplesPerMillisecond * sizeof(short);
        reader.BaseStream.ReadExactly(bytes);
        var waveform = new float[sampleCount];
        double sum = 0;
        double squares = 0;
        for (var i = 0; i < waveform.Length; i++)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * sizeof(short))) / 32768f;
            waveform[i] = value;
            sum += value;
            squares += value * value;
        }
        var mean = sum / sampleCount;
        var variance = Math.Max(0, squares / sampleCount - mean * mean);
        if (variance < 1e-9)
            return null;
        var scale = 1 / Math.Sqrt(variance + 1e-7);
        for (var i = 0; i < waveform.Length; i++)
            waveform[i] = (float)((waveform[i] - mean) * scale);
        return NamedOnnxValue.CreateFromTensor("input_values", new DenseTensor<float>(waveform, [1, sampleCount]));
    }

    // WhisperX forced alignment: use the most probable blank/character path through CTC emissions.
    private static CharacterTiming[]? FindCharacterTimes(Span<float> emissions, int frames, List<int> tokens,
        bool allowLeadingSpeech)
    {
        var width = tokens.Count + 1;
        var trellis = new float[(frames + 1) * width];
        trellis.AsSpan(1, tokens.Count).Fill(float.NegativeInfinity);
        var best = float.NegativeInfinity;
        var bestFrame = 0;
        for (var t = 0; t < frames; t++)
        {
            var logits = emissions.Slice(t * VocabularySize, VocabularySize);
            var maximum = float.NegativeInfinity;
            foreach (var value in logits)
                maximum = Math.Max(maximum, value);
            var total = 0f;
            foreach (var value in logits)
                total += MathF.Exp(value - maximum);
            var normalizer = maximum + MathF.Log(total);
            for (var label = 0; label < logits.Length; label++)
                logits[label] -= normalizer;

            var current = t * width;
            var next = current + width;
            trellis[next] = allowLeadingSpeech ? 0 : trellis[current] + logits[0];
            for (var j = 1; j < width; j++)
                trellis[next + j] = Math.Max(trellis[current + j] + logits[0],
                    trellis[current + j - 1] + logits[tokens[j - 1]]);

            if (trellis[next + width - 1] > best)
            {
                best = trellis[next + width - 1];
                bestFrame = t + 1;
            }
        }

        var characters = new CharacterTiming[tokens.Count];
        var token = tokens.Count;
        var end = bestFrame;
        var score = 0f;
        for (var frame = bestFrame - 1; frame >= 0; frame--)
        {
            var logits = emissions.Slice(frame * VocabularySize, VocabularySize);
            var previous = frame * width;
            var blank = trellis[previous + token] + logits[0];
            var match = trellis[previous + token - 1] + logits[tokens[token - 1]];
            if (match <= blank)
            {
                score += MathF.Exp(logits[0]);
                continue;
            }

            score += MathF.Exp(logits[tokens[token - 1]]);
            characters[--token] = new(frame, end, score / (end - frame));
            if (token == 0)
                return characters;
            end = frame;
            score = 0;
        }
        return null;
    }

    private static bool ReadPcmHeader(BinaryReader reader, out long start, out long samples)
    {
        start = samples = 0;
        var stream = reader.BaseStream;
        if (stream.Length < 12 || reader.ReadUInt32() != 0x46464952) // RIFF
            return false;
        reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x45564157) // WAVE
            return false;

        var pcm = false;
        while (stream.Length - stream.Position >= 8)
        {
            var chunk = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            var payload = stream.Position;
            if (length > stream.Length - payload)
                return false;
            if (chunk == 0x20746D66 && length >= 16) // fmt
            {
                var format = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var sampleRate = reader.ReadUInt32();
                reader.ReadUInt32();
                var blockSize = reader.ReadUInt16();
                var bitsPerSample = reader.ReadUInt16();
                pcm = format == 1 && channels == 1 && sampleRate == 16000 && blockSize == 2 && bitsPerSample == 16;
            }
            if (chunk == 0x61746164) // data
            {
                start = payload;
                samples = length / sizeof(short);
                return pcm;
            }
            stream.Position = payload + length + (length & 1);
        }
        return false;
    }

    private sealed record CharacterTiming(int Start, int End, float Confidence);
    private sealed record SpelledWord(string Text, long Start, float[] Confidence);
}
