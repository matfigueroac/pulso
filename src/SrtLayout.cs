using System;
using System.Collections.Generic;
using System.Linq;

namespace LumenPlayer;

internal static class SrtLayout
{
    private const int LineLength = 42;
    private const long MaximumDuration = 7000;
    private static readonly HashSet<string> ShortConnectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "of", "in", "on", "at", "for", "with", "and", "or", "but", "my", "your"
    };

    internal static IEnumerable<Block> Create(IEnumerable<SpeechRecognition.CaptionCue> cues, long audioEnd)
    {
        var ordered = cues.Where(cue => cue.Start >= 0 && cue.End > cue.Start && cue.Start < audioEnd)
            .OrderBy(cue => cue.Start).ThenByDescending(cue => cue.WeakestToken)
            .DistinctBy(cue => cue.Start).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var cue = ordered[index];
            var next = index + 1 < ordered.Length ? ordered[index + 1].Start : audioEnd;
            var words = cue.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var end = Math.Min(cue.End, next);
            var offsets = new int[words.Length + 1];
            for (var i = 0; i < words.Length; i++)
                offsets[i + 1] = offsets[i] + words[i].Length + 1;

            var times = cue.WordStarts;
            var ends = cue.WordEnds;
            var timed = times is not null && times.Length == words.Length;
            for (var i = 1; timed && i < words.Length; i++)
                timed = times![i] > Math.Max(cue.Start, times[i - 1]) && times[i] < end;

            for (var first = 0; first < words.Length;)
            {
                var start = TimeAt(first);
                var limit = first + 1;
                while (limit < words.Length && !IsSentenceBoundary(first, limit) && TimeAt(limit + 1) - start <= MaximumDuration
                    && Wrap(words, offsets, first, limit + 1) is not null)
                    limit++;

                // Prefer a pause in the text near a required break.
                for (var candidate = limit - 1; limit < words.Length && !IsSentenceBoundary(first, limit) && candidate > first
                    && offsets[candidate] - offsets[first] >= 0.70 * (offsets[limit] - offsets[first]); candidate--)
                {
                    if (!HasPunctuation(words[candidate - 1]))
                        continue;
                    limit = candidate;
                    break;
                }

                var nextStart = limit < words.Length ? TimeAt(limit) : next;
                var sentenceBreak = IsSentenceBoundary(first, limit);
                var speechEnd = sentenceBreak ? Math.Min(ends![limit - 1], nextStart) : TimeAt(limit);
                if (speechEnd > start)
                {
                    var text = Wrap(words, offsets, first, limit)!;
                    var outTime = speechEnd;
                    if (limit == words.Length || sentenceBreak)
                    {
                        var readingTime = Math.Max(850, (offsets[limit] - offsets[first] - 1) * 50L);
                        var hold = Math.Clamp(readingTime - (speechEnd - start), 120, 850);
                        outTime += Math.Min(hold, Math.Max(0, nextStart - speechEnd - 50));
                    }
                    yield return new(start, Math.Min(outTime, start + MaximumDuration), text);
                }
                first = limit;
            }

            long TimeAt(int word) => word == 0 ? cue.Start : word == words.Length ? end : timed ? times![word]
                : cue.Start + (end - cue.Start) * offsets[word] / offsets[^1];

            bool IsSentenceBoundary(int first, int word) => word < words.Length && timed && ends?.Length == words.Length
                && words[word - 1].TrimEnd('"', '”', '’', ')', ']') is { Length: > 0 } previous
                && previous[^1] is '.' or '?' or '!'
                && (word - first >= 3 || times![word] - ends[word - 1] >=
                    (word - first == 1 && previous[^1] == '!' ? 1000 : 280));
        }
    }

    private static string? Wrap(string[] words, int[] offsets, int first, int end)
    {
        if (end == first + 1 || offsets[end] - offsets[first] - 1 <= LineLength)
            return string.Join(' ', words, first, end - first);

        var bestScore = int.MaxValue;
        var best = -1;
        for (var split = first + 1; split < end; split++)
        {
            var left = offsets[split] - offsets[first] - 1;
            var right = offsets[end] - offsets[split] - 1;
            if (left > LineLength || right > LineLength)
                continue;
            var previous = words[split - 1];
            var score = Math.Abs(right - left) + (left > right ? 3 : 0)
                + (ShortConnectors.Contains(previous) ? 22 : 0) - (HasPunctuation(previous) ? 14 : 0);
            if (score < bestScore)
            {
                best = split;
                bestScore = score;
            }
        }
        return best < 0 ? null : string.Join(' ', words, first, best - first) + "\n" + string.Join(' ', words, best, end - best);
    }

    private static bool HasPunctuation(string word) => word.TrimEnd('"', '\'', '”', '’', ')', ']') is { Length: > 0 } text
        && text[^1] is ',' or ';' or ':' or '.' or '?' or '!';

    internal sealed record Block(long Start, long End, string Text);
}
