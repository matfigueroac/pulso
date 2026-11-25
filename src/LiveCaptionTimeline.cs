using System;
using System.Collections.Generic;
using System.Linq;

namespace LumenPlayer;

internal sealed class LiveCaptionTimeline
{
    public const long MaximumDelay = 10_000;
    private readonly List<DisplayCue> _cues = [];
    private long _shownThrough = -1;

    public void Clear()
    {
        _cues.Clear();
        _shownThrough = -1;
    }

    public string? TextAt(long playbackPosition, long delay = 0)
    {
        _cues.RemoveAll(cue => cue.End < playbackPosition - MaximumDelay - 2000);
        var position = playbackPosition - delay;
        var current = _cues.LastOrDefault(cue => cue.Start <= position && cue.End > position);
        if (current is not null)
            _shownThrough = Math.Max(_shownThrough, position);
        return current?.Text;
    }

    public void AppendWindow(IEnumerable<SpeechRecognition.CaptionCue> recognized, long start, long end,
        long? resumeAt = null)
    {
        var committedEnd = _cues.LastOrDefault()?.SpeechEnd ?? start;
        foreach (var cue in recognized)
        {
            if (cue.Start >= end || cue.End <= start)
                continue;

            var words = cue.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                continue;
            var onset = cue.Start;
            var wordStarts = cue.WordStarts is { } times && times.Length == words.Length ? times : null;
            // The first decode after a seek includes earlier audio for recognition context.
            if (resumeAt is long position && onset < start && wordStarts is { } wordTimes
                && wordTimes.Length == words.Length)
            {
                var firstWord = ResumeWord(words, wordTimes, position);
                if (firstWord > 0)
                {
                    onset = wordTimes[firstWord];
                    words = words[firstWord..];
                    wordStarts = wordTimes[firstWord..];
                }
            }
            var normalized = words.Select(NormalizeWord).ToArray();
            var previous = _cues.LastOrDefault();
            // Reconcile only the shared audio at the seam. Repeated dialogue later is a new cue.
            if (previous is not null && onset < previous.SpeechEnd && onset < committedEnd)
            {
                var repeated = SharedWords(previous, normalized, wordStarts, onset);
                if (repeated == words.Length || cue.End <= previous.SpeechEnd)
                    continue;
                // A window can end mid-sentence. The next window may contain the
                // same opening words plus the continuation, with its full timing.
                if (previous.Start > _shownThrough && repeated >= 3 && repeated == previous.Words.Length
                    && Math.Abs(onset - previous.Start) <= 200
                    && wordStarts is { } continuation && continuation.Length == words.Length
                    && continuation[repeated] >= previous.SpeechEnd - 120)
                {
                    _cues[^1] = previous with
                    {
                        End = Math.Max(cue.End + 120, previous.Start + 850),
                        SpeechEnd = cue.End,
                        Text = FormatLines(words),
                        Words = normalized,
                        WordStarts = continuation
                    };
                    committedEnd = cue.End;
                    continue;
                }
                if (previous.SpeechEnd - onset >= 350 && wordStarts is { } starts && starts.Length == words.Length)
                {
                    // The two windows can disagree about the words they share. Trim the overlap
                    // by timing too, so the earlier audio is not associated with a brand new cue.
                    while (repeated < starts.Length && starts[repeated] < previous.SpeechEnd - 120)
                        repeated++;
                }
                if (repeated == words.Length)
                    continue;
                if (repeated > 0)
                {
                    onset = wordStarts is not null && wordStarts.Length == words.Length
                        ? wordStarts[repeated] : onset + (cue.End - onset) * repeated / words.Length;
                    words = words[repeated..];
                    normalized = normalized[repeated..];
                    wordStarts = wordStarts?[repeated..];
                }
                onset = Math.Max(onset, previous.SpeechEnd);
            }
            else if (previous is not null)
            {
                onset = Math.Max(onset, previous.SpeechEnd);
            }
            if (onset >= end || cue.End - onset < 140)
                continue;

            // Keep short lines readable. The next cue takes precedence as soon as it begins.
            var displayEnd = Math.Max(cue.End + 120, onset + 850);
            _cues.Add(new(onset, displayEnd, cue.End, FormatLines(words), normalized, wordStarts));
        }
    }

    private static int ResumeWord(string[] words, long[] starts, long position)
    {
        var first = 0;
        for (var i = 1; i < words.Length && starts[i] <= position; i++)
        {
            if (IsPhraseBreak(words[i - 1], i - first, starts[i] - starts[i - 1]))
                first = i;
        }
        return first;
    }

    internal static bool IsPhraseBreak(string previousWord, int wordCount, long gap)
    {
        if (gap <= 0)
            return false;
        // Keep a short call such as “Walter!” with the phrase it introduces.
        var word = previousWord.TrimEnd('"', '\'', '”', '’', ')', ']');
        if (wordCount == 1 && gap < 1200 && word.Length > 0 && (word[^1] is '!' or ','))
            return false;
        return word.Length > 0 && (word[^1] is '.' or '?' or '!'
            || (word[^1] is ',' or ';' or ':' && gap >= 500));
    }

    private static int SharedWords(DisplayCue previous, string[] next, long[]? nextTimes, long onset)
    {
        for (var count = Math.Min(previous.Words.Length, next.Length); count > 0; count--)
        {
            var first = previous.Words.Length - count;
            if (!previous.Words.AsSpan(first, count).SequenceEqual(next.AsSpan(0, count)))
                continue;
            if (previous.WordStarts is not { } previousTimes || nextTimes is null)
            {
                if (count >= 3 && previous.SpeechEnd - onset >= 350)
                    return count;
                continue;
            }
            var i = 0;
            while (i < count && Math.Abs(previousTimes[first + i] - nextTimes[i]) <= (count == 1 ? 120 : 250))
                i++;
            if (i == count)
                return count;
        }
        return 0;
    }

    internal static string NormalizeWord(string word) => string.Concat(word.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    internal static string FormatLines(string[] words)
    {
        var text = string.Join(' ', words);
        if (text.Length <= 38 || words.Length < 2)
            return text;

        var leftLength = 0;
        var bestBreak = 1;
        var bestScore = int.MaxValue;
        for (var index = 1; index < words.Length; index++)
        {
            leftLength += words[index - 1].Length + (index == 1 ? 0 : 1);
            var rightLength = text.Length - leftLength - 1;
            var score = Math.Abs(leftLength - rightLength) + Math.Max(0, Math.Max(leftLength, rightLength) - 42) * 3;
            if (words[index - 1][^1] is ',' or ';' or ':' or '.' or '?' or '!')
                score -= 5;
            if (score >= bestScore)
                continue;
            bestBreak = index;
            bestScore = score;
        }
        return string.Join(' ', words[..bestBreak]) + "\n" + string.Join(' ', words[bestBreak..]);
    }

    private sealed record DisplayCue(long Start, long End, long SpeechEnd, string Text, string[] Words, long[]? WordStarts);
}
