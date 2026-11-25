using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private readonly LiveCaptionTimeline _liveTimeline = new();
    private readonly DispatcherTimer _captionClock = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
    private bool _liveCaptions;
    private CancellationTokenSource? _liveWork;
    private SpeechRecognition.AudioStream? _liveAudioStream;
    private int? _liveAudioTrack;
    private int? _previousSubtitleTrack;
    private long? _liveNextWindow;
    private long? _liveWindowStart;
    private long _liveCaptionDelayMs;
    private long _lastLivePosition = -1;
    private long _lastLiveTick;
    private bool _liveHasResults;

    private void OnToggleLiveCaptions(object sender, RoutedEventArgs e) => ToggleLiveCaptions();

    private void ToggleLiveCaptions()
    {
        if (!_liveCaptions && !SpeechRecognition.SupportsLiveCaptions)
        {
            SetStatus("Motor de subtítulos no disponible.", error: true);
            return;
        }
        _pendingSubtitleMode = null;
        SetLiveCaptions(!_liveCaptions);
        SavePlaybackBookmark();
    }

    private void SetLiveCaptions(bool enabled, bool announce = true, bool cancelGeneration = true)
    {
        if (_liveCaptions == enabled)
            return;
        if (enabled && _requestedPlayback && CurrentSubtitleTrack is >= 0 and var previous)
        {
            _previousSubtitleTrack = previous;
            SelectSubtitleTrack(-1);
        }
        _liveCaptions = enabled;
        ResetLiveWindow();
        if (enabled)
        {
            if (cancelGeneration)
                _subtitleGeneration?.Cancel();
            _captionClock.Start();
        }
        else
        {
            _captionClock.Stop();
            if (_requestedPlayback && _previousSubtitleTrack is >= 0 and var track)
                SelectSubtitleTrack(track);
            _previousSubtitleTrack = null;
        }

        var label = enabled ? "Desactivar CC en vivo" : "Activar CC en vivo (inglés)";
        SubtitleButton.ToolTip = label;
        AutomationProperties.SetName(SubtitleButton, label);
        CcLabel.Foreground = (Brush)FindResource(enabled ? "AccentBrush" : "TextBrush");
        CcBorder.BorderBrush = (Brush)FindResource(enabled ? "AccentBrush" : "MutedBrush");
        if (announce)
            SetStatus(enabled ? "CC en vivo · reconociendo diálogo…" : "CC en vivo desactivado", activity: "SubtitleTrack");
    }

    private void ResetLiveWindow()
    {
        _liveWork?.Cancel();
        _liveWork = null;
        _liveTimeline.Clear();
        _liveNextWindow = null;
        _liveWindowStart = null;
        _liveAudioStream = null;
        _liveAudioTrack = null;
        _lastLivePosition = -1;
        _liveHasResults = false;
        LiveCaptionBox.Visibility = Visibility.Collapsed;
    }

    private void RefreshLiveCaptions()
    {
        if (!_liveCaptions || !_videoVisible || !_requestedPlayback || _current is null
            || _player is null || _player.State is not (VLCState.Playing or VLCState.Paused))
            return;
        if (_draggingSeek)
        {
            LiveCaptionBox.Visibility = Visibility.Collapsed;
            return;
        }

        var position = Math.Max(0, _player.Time);
        var now = Environment.TickCount64;
        var delta = position - _lastLivePosition;
        var track = _player.AudioTrack;
        var jumped = _lastLivePosition >= 0 && (delta < -750 || delta > (now - _lastLiveTick) * _rate + 1400);
        if (jumped || _liveAudioTrack != track)
            ResetLiveWindow();
        _liveAudioTrack = track;
        _lastLivePosition = position;
        _lastLiveTick = now;

        if (_player.Spu >= 0)
        {
            if (_startupSubtitle is not null || _loadingSubtitle is not null)
                RefreshSubtitleSync();
            _previousSubtitleTrack ??= _player.Spu;
            SelectSubtitleTrack(-1);
        }

        var caption = _liveTimeline.TextAt(position, _liveCaptionDelayMs);
        ShowSubtitleCaption(caption);

        var duration = _player.Length;
        var captionPosition = Math.Max(0, position - _liveCaptionDelayMs);
        if (_liveWork is not null || _subtitleGeneration is not null || duration <= position
            || (_liveHasResults && !_player.IsPlaying && _liveNextWindow > captionPosition))
            return;
        var next = _liveNextWindow ?? Math.Max(0, captionPosition - 300);
        if (next >= duration || next - Math.Max(position, captionPosition) >= Math.Max(10000, 11000 * _rate))
            return;
        if (captionPosition - next > 3500)
        {
            next = Math.Max(0, captionPosition - 300);
            _liveWindowStart = next;
        }
        _liveWindowStart ??= next;
        ReadLiveWindow(_current.Location, GetAudioOrdinal(track), next, duration,
            _liveHasResults ? null : captionPosition);
    }

    private int GetAudioOrdinal(int track) => track < 0 ? -1
        : Array.FindIndex(_media?.Tracks.Where(info => info.TrackType == TrackType.Audio).ToArray() ?? [],
            info => info.Id == track);

    private async void ReadLiveWindow(string input, int ordinal, long next, long duration, long? resumeAt)
    {
        using var work = new CancellationTokenSource();
        _liveWork = work;
        var advance = Math.Min(duration, next + (_liveHasResults ? 8500 : 5500));
        try
        {
            var audio = _liveAudioStream ?? await SpeechRecognition.FindAudioStream(input, ordinal, work.Token);
            var cues = await SpeechRecognition.RecognizeWindow(input, audio, Math.Max(0, next - 1700),
                Math.Min(duration, advance + 1800), resumeAt, work.Token);
            if (!ReferenceEquals(_liveWork, work))
                return;
            _liveAudioStream = audio;
            _liveNextWindow = advance;
            _liveTimeline.AppendWindow(cues, next, advance, resumeAt);
            if (!_liveHasResults && _liveCaptionDelayMs == 0)
                SetStatus("CC en vivo activado · inglés");
            _liveHasResults = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (ReferenceEquals(_liveWork, work))
            {
                _liveWork = null;
                SetLiveCaptions(false, announce: false);
                SetStatus(ex is InvalidDataException ? ex.Message : "No se pudieron activar los CC en vivo.", error: true);
                Trace.TraceWarning(ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_liveWork, work))
                _liveWork = null;
        }
    }
}
