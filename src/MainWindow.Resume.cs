using System;
using System.Globalization;
using System.Linq;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private const string PlaybackBookmarkKey = "playback-bookmark";
    private const string LiveSubtitleMode = "live";
    private const string DisabledSubtitleMode = "off";
    private const string ExternalSubtitlePrefix = "file:";
    private const string EmbeddedSubtitlePrefix = "track:";
    private PlaybackBookmark? _lastSavedBookmark;
    private string? _pendingSubtitleMode;
    private long _restoreDeadline;
    private long _nextBookmarkSave;
    private readonly ResumePosition _resume = new();
    private string? _lastSubtitleMode;

    private void PreparePlaybackBookmark(QueueEntry entry)
    {
        var bookmark = entry.SubtitleSync.Read<PlaybackBookmark>(PlaybackBookmarkKey);
        _lastSavedBookmark = bookmark;
        _restoreDeadline = 0;
        _nextBookmarkSave = Environment.TickCount64 + 5000;
        _resume.Restore(bookmark, Environment.TickCount64);
        _pendingSubtitleMode = entry.TakeExplicitSubtitles() ? null : bookmark?.SubtitleMode;
        _lastSubtitleMode = _pendingSubtitleMode;
        var restoreLive = _pendingSubtitleMode == LiveSubtitleMode && SpeechRecognition.SupportsLiveCaptions;
        SetLiveCaptions(restoreLive, announce: false, cancelGeneration: false);
    }

    private void ApplyBookmarkStartTime(Media media)
    {
        if (_resume.Position > 0)
            media.AddOption(":start-time=" + (_resume.Position / 1000d).ToString("0.000", CultureInfo.InvariantCulture));
    }

    private void RestoreSubtitleMode()
    {
        if (_pendingSubtitleMode is not { } mode || _player is null)
            return;

        var restored = mode switch
        {
            LiveSubtitleMode => _liveCaptions,
            DisabledSubtitleMode => _player.SpuDescription.Any(track => track.Id >= 0) && SelectSubtitleTrack(-1),
            _ when mode.StartsWith(ExternalSubtitlePrefix, StringComparison.Ordinal) =>
                _subtitleFiles.TryGetValue(CurrentSubtitleTrack, out var path)
                && string.Equals(path, mode[ExternalSubtitlePrefix.Length..], StringComparison.OrdinalIgnoreCase),
            _ when mode.StartsWith(EmbeddedSubtitlePrefix, StringComparison.Ordinal)
                && int.TryParse(mode.AsSpan(EmbeddedSubtitlePrefix.Length), out var id) && id >= 0 => SelectSubtitleTrack(id),
            _ => true
        };

        if (restored || Environment.TickCount64 >= _restoreDeadline)
        {
            _pendingSubtitleMode = null;
            ApplySubtitleSync();
        }
    }

    private void RefreshPlaybackBookmark(VLCState state)
    {
        _restoreDeadline = _restoreDeadline == 0 ? Environment.TickCount64 + 2500 : _restoreDeadline;
        RestoreSubtitleMode();
        UpdateBookmarkPosition();
        _lastSubtitleMode = CurrentSubtitleMode();
        if (Environment.TickCount64 >= _nextBookmarkSave || (state == VLCState.Paused && _lastState != state))
            SavePlaybackBookmark();
    }

    private void UpdateBookmarkPosition()
    {
        if (_player is { IsSeekable: true })
            _resume.Observe(_player.Time, Environment.TickCount64);
    }

    private void SavePlaybackBookmark()
    {
        if (_current is null || _player is null || _queue.Request is not { } request)
            return;

        var state = _player.State;
        if (state == VLCState.Error || (state == VLCState.Ended && !CompletedPlayback(request)))
            return;
        if (state is VLCState.Playing or VLCState.Paused)
        {
            UpdateBookmarkPosition();
            _lastSubtitleMode = CurrentSubtitleMode();
        }

        var bookmark = _resume.Bookmark(request, state == VLCState.Ended, _lastSubtitleMode);
        if (bookmark is not null && bookmark != _lastSavedBookmark && _current.SubtitleSync.Save(PlaybackBookmarkKey, bookmark))
            _lastSavedBookmark = bookmark;
        _nextBookmarkSave = Environment.TickCount64 + 5000;
    }

    private string? CurrentSubtitleMode()
    {
        if (_pendingSubtitleMode is not null || _player is null)
            return _pendingSubtitleMode;
        if (_liveCaptions)
            return LiveSubtitleMode;
        var id = CurrentSubtitleTrack;
        if (id < 0)
            return DisabledSubtitleMode;
        return _subtitleFiles.TryGetValue(id, out var path) ? ExternalSubtitlePrefix + path : EmbeddedSubtitlePrefix + id;
    }

}
