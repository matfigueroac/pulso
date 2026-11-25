using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LumenPlayer;

internal static class SubtitlePairing
{
    private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public static int FindOwner(string subtitle, IReadOnlyList<string> mediaPaths)
    {
        var subtitleStem = Path.GetFileNameWithoutExtension(subtitle);
        var subtitleDirectory = Path.GetDirectoryName(subtitle);
        return Enumerable.Range(0, mediaPaths.Count)
            .Select(index =>
            {
                var path = mediaPaths[index];
                var name = Path.GetFileName(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                var sameDirectory = string.Equals(Path.GetDirectoryName(path), subtitleDirectory, PathComparison);
                var matchLength = Matches(subtitleStem, name) ? name.Length
                    : Matches(subtitleStem, stem) ? stem.Length : 0;
                return (Index: index, SameDirectory: sameDirectory, MatchLength: matchLength);
            })
            .Where(candidate => candidate.MatchLength > 0)
            .OrderByDescending(candidate => candidate.SameDirectory)
            .ThenByDescending(candidate => candidate.MatchLength)
            .Select(candidate => candidate.Index)
            .FirstOrDefault();
    }

    private static bool Matches(string subtitleStem, string mediaName) =>
        subtitleStem.Equals(mediaName, PathComparison) || subtitleStem.StartsWith(mediaName + ".", PathComparison);
}
