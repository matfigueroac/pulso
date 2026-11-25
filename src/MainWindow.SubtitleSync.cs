using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private const string LiveDelayKey = "live-delay";
    private const string ExternalSubtitleKey = "external-subtitle";
    private readonly Dictionary<int, string> _subtitleFiles = [];
    private readonly Queue<(string Path, bool Select)> _subtitleLoads = new();
    private (string Path, bool Select, HashSet<int> KnownTracks, long Deadline)? _loadingSubtitle;
    private string? _startupSubtitle;
    private string? _appliedSubtitleKey;

    private static string? FindAdjacentSrt(string video)
    {
        var name = Path.ChangeExtension(video, null);
        string[] candidates = [name + ".srt", video + ".srt", name + ".en.srt", name + ".eng.srt", name + "_eng.srt"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private void PrepareSubtitleSync(QueueEntry entry)
    {
        _srtTracks.Clear();
        _subtitleFiles.Clear();
        _subtitleLoads.Clear();
        _loadingSubtitle = null;
        _startupSubtitle = null;
        _appliedSubtitleKey = null;
        _subtitleDelayMs = 0;
        _liveCaptionDelayMs = Math.Clamp(entry.SubtitleSync.Read<long>(LiveDelayKey),
            -LiveCaptionTimeline.MaximumDelay, LiveCaptionTimeline.MaximumDelay);
    }

    private void PrepareStartupSubtitles(QueueEntry entry)
    {
        var remembered = entry.SubtitleSync.Read<string>(ExternalSubtitleKey);
        var automatic = SpeechRecognition.FindGeneratedSubtitle(entry.Location) ?? FindAdjacentSrt(entry.Location);
        var attached = entry.Subtitles.Where(File.Exists).ToArray();
        var savedFile = _pendingSubtitleMode?.StartsWith(ExternalSubtitlePrefix, StringComparison.Ordinal) == true
            ? _pendingSubtitleMode[ExternalSubtitlePrefix.Length..] : null;
        _startupSubtitle = _pendingSubtitleMode?.StartsWith(EmbeddedSubtitlePrefix, StringComparison.Ordinal) == true
            ? null : File.Exists(savedFile) ? savedFile : attached.LastOrDefault() ?? (File.Exists(remembered) ? remembered : automatic);
        if (_startupSubtitle is not null)
            _media!.AddOption(":sub-file=" + _startupSubtitle);
        foreach (var path in attached.Where(path => !string.Equals(path, _startupSubtitle, StringComparison.OrdinalIgnoreCase)))
            _subtitleLoads.Enqueue((path, false));
    }

    private void RefreshSubtitleSync()
    {
        if (_player is null || _current is null)
            return;

        if (_startupSubtitle is { } startup && _player.Spu >= 0)
        {
            _subtitleFiles[_player.Spu] = startup;
            _current.SubtitleSync.Save(ExternalSubtitleKey, startup);
            _startupSubtitle = null;
        }

        string? selectedFile = null;
        if (_loadingSubtitle is { } pending)
        {
            var added = _player.SpuDescription.FirstOrDefault(track => track.Id >= 0 && !pending.KnownTracks.Contains(track.Id));
            if (added.Name is not null)
            {
                _loadingSubtitle = null;
                _subtitleFiles[added.Id] = pending.Path;
                if (pending.Select)
                {
                    _current.SubtitleSync.Save(ExternalSubtitleKey, pending.Path);
                    if (!_liveCaptions)
                    {
                        if (SelectSubtitleTrack(added.Id))
                            selectedFile = pending.Path;
                        else
                            SetStatus("No se pudo seleccionar " + Path.GetFileName(pending.Path), error: true);
                    }
                }
            }
            else if (Environment.TickCount64 >= pending.Deadline)
            {
                _loadingSubtitle = null;
                SetStatus("No se pudo cargar " + Path.GetFileName(pending.Path), error: true);
            }
        }

        ApplySubtitleSync();
        if (selectedFile is not null)
        {
            SavePlaybackBookmark();
            ShowSelectedSubtitle(selectedFile);
        }
        if (_loadingSubtitle is null && _subtitleLoads.TryDequeue(out var request))
            LoadSubtitleTrack(request.Path, request.Select);
    }

    private void LoadSubtitleTrack(string path, bool select)
    {
        if (_player is null || _current is null)
            return;
        foreach (var (id, existing) in _subtitleFiles)
        {
            if (!string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!select)
                return;
            if (SelectSubtitleTrack(id))
            {
                _current.SubtitleSync.Save(ExternalSubtitleKey, path);
                ApplySubtitleSync();
                SavePlaybackBookmark();
                ShowSelectedSubtitle(path);
            }
            else
                SetStatus("No se pudo seleccionar " + Path.GetFileName(path), error: true);
            return;
        }
        var known = _player.SpuDescription.Select(track => track.Id).ToHashSet();
        if (_player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, false))
            _loadingSubtitle = (path, select, known, Environment.TickCount64 + 5000);
        else
            SetStatus("No se pudo cargar " + Path.GetFileName(path), error: true);
    }

    private void ShowSelectedSubtitle(string path) =>
        SetStatus($"Subtítulos: {Path.GetFileName(path)} · {FormatDelay(_subtitleDelayMs)}", activity: "SubtitleTrack");

    private string? SelectedSubtitleKey()
    {
        var id = CurrentSubtitleTrack;
        if (id < 0)
            return null;
        return _subtitleFiles.TryGetValue(id, out var path) ? "file:" + path.ToUpperInvariant() : $"track:{id}";
    }

    private void ApplySubtitleSync()
    {
        if (_liveCaptions || _current is null || _player is null)
            return;
        var key = SelectedSubtitleKey();
        if (key is null)
        {
            _appliedSubtitleKey = null;
            return;
        }
        if (key == _appliedSubtitleKey)
            return;
        var saved = _current.SubtitleSync.Read<long>(key);
        if (_displayedSrt is not null || _player.SetSpuDelay(saved * 1000))
        {
            _appliedSubtitleKey = key;
            _subtitleDelayMs = saved;
        }
    }
}
