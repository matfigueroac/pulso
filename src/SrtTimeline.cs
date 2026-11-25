using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LumenPlayer;

internal sealed class SrtTimeline
{
    private static readonly Regex Separator = new(@"\n[\t ]*\n(?:[\t ]*\n)*", RegexOptions.CultureInvariant);
    private static readonly Regex Timing = new(
        @"^\s*([0-9]{1,6}:[0-9]{2}:[0-9]{2}[,.][0-9]{3})\s*-->\s*([0-9]{1,6}:[0-9]{2}:[0-9]{2}[,.][0-9]{3})(?:\s|$)",
        RegexOptions.CultureInvariant);
    private static readonly Regex Markup = new(@"</?(?:b|i|u|s|font)\b[^<>]*>|\{\\an[1-9]\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly Cue[] _cues;
    private readonly long[] _latestEnd;

    private SrtTimeline(Cue[] cues)
    {
        _cues = cues;
        _latestEnd = new long[cues.Length];
        long end = 0;
        for (var index = 0; index < cues.Length; index++)
        {
            end = Math.Max(end, cues[index].End);
            _latestEnd[index] = end;
        }
    }

    internal static async Task<SrtTimeline?> ReadFile(string path, CancellationToken cancellation)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), cancellation).ConfigureAwait(false);
        }
        catch (DecoderFallbackException)
        {
            var windowsEncoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
            content = await File.ReadAllTextAsync(path, windowsEncoding, cancellation).ConfigureAwait(false);
        }
        return Parse(content);
    }

    internal static SrtTimeline? Parse(string content)
    {
        var cues = new List<Cue>();
        foreach (var block in Separator.Split(content.ReplaceLineEndings("\n").TrimStart('\uFEFF')))
        {
            var lines = block.Trim().Split('\n');
            var timingIndex = int.TryParse(lines[0], out _) ? 1 : 0;
            if (lines.Length <= timingIndex + 1)
                continue;

            var timing = Timing.Match(lines[timingIndex]);
            if (!timing.Success)
                continue;

            var start = ReadTime(timing.Groups[1].ValueSpan);
            var end = ReadTime(timing.Groups[2].ValueSpan);
            var text = WebUtility.HtmlDecode(Markup.Replace(string.Join('\n', lines.Skip(timingIndex + 1)), "")).Trim();
            if (end > start && text.Length > 0)
                cues.Add(new(start, end, text));
        }
        return cues.Count == 0 ? null : new(cues.OrderBy(cue => cue.Start).ToArray());
    }

    internal string? TextAt(long position)
    {
        var left = 0;
        var right = _cues.Length;
        while (left < right)
        {
            var middle = (left + right) / 2;
            if (_cues[middle].Start <= position)
                left = middle + 1;
            else
                right = middle;
        }

        string? text = null;
        for (var index = left - 1; index >= 0 && _latestEnd[index] > position; index--)
        {
            var cue = _cues[index];
            if (cue.End > position)
                text = text is null ? cue.Text : cue.Text + "\n" + text;
        }
        return text;
    }

    private static long ReadTime(ReadOnlySpan<char> value)
    {
        var hoursEnd = value.IndexOf(':');
        var remainder = value[(hoursEnd + 1)..];
        var hours = long.Parse(value[..hoursEnd], CultureInfo.InvariantCulture);
        var minutes = int.Parse(remainder[..2], CultureInfo.InvariantCulture);
        var seconds = int.Parse(remainder.Slice(3, 2), CultureInfo.InvariantCulture);
        var milliseconds = int.Parse(remainder[6..], CultureInfo.InvariantCulture);
        return ((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds;
    }

    private sealed record Cue(long Start, long End, string Text);
}
