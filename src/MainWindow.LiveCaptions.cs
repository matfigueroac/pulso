using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private readonly List<SpeechRecognition.CaptionCue> _liveCues = [];
    private bool _liveCaptions;
    private CancellationTokenSource? _liveWork;
    private string? _liveAudioStream;
    private int? _liveAudioTrack;
    private int? _previousSubtitleTrack;
    private long? _liveNextWindow;
    private long _lastLivePosition = -1;
    private long _lastLiveTick;
    private bool _liveHasResults;

    private void OnToggleLiveCaptions(object sender, RoutedEventArgs e) => ToggleLiveCaptions();

    private void ToggleLiveCaptions()
    {
        if (!_liveCaptions && !SpeechRecognition.IsAvailable)
        {
            SetStatus("Motor de subtítulos no disponible.", error: true);
            return;
        }
        SetLiveCaptions(!_liveCaptions);
    }

    private void SetLiveCaptions(bool enabled, bool announce = true)
    {
        if (_liveCaptions == enabled)
            return;
        _liveCaptions = enabled;
        ResetLiveWindow();
        if (enabled)
            _subtitleGeneration?.Cancel();
        else
        {
            if (_requestedPlayback && _previousSubtitleTrack is >= 0 and var track)
                _player?.SetSpu(track);
            _previousSubtitleTrack = null;
        }

        var label = enabled ? "Desactivar CC en vivo" : "Activar CC en vivo (inglés)";
        SubtitleButton.ToolTip = label;
        AutomationProperties.SetName(SubtitleButton, label);
        CcLabel.Foreground = (Brush)FindResource(enabled ? "AccentBrush" : "TextBrush");
        CcBorder.BorderBrush = (Brush)FindResource(enabled ? "AccentBrush" : "MutedBrush");
        if (announce)
            SetStatus(enabled ? "CC en vivo activado · inglés" : "CC en vivo desactivado");
    }

    private void ResetLiveWindow()
    {
        _liveWork?.Cancel();
        _liveWork = null;
        _liveCues.Clear();
        _liveNextWindow = null;
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
            _previousSubtitleTrack ??= _player.Spu;
            _player.SetSpu(-1);
        }

        _liveCues.RemoveAll(cue => cue.End < position - 2000);
        var caption = _liveCues.FirstOrDefault(cue => cue.Start <= position && cue.End > position)?.Text;
        if (caption is null && !_liveHasResults)
            caption = "Reconociendo diálogo…";
        LiveCaptionText.Text = caption ?? "";
        LiveCaptionBox.Visibility = caption is null ? Visibility.Collapsed : Visibility.Visible;

        var duration = _player.Length;
        if (_liveWork is not null || _subtitleGeneration is not null
            || (_liveHasResults && !_player.IsPlaying) || duration <= position)
            return;
        var next = _liveNextWindow ?? Math.Max(0, position - 300);
        if (next >= duration || next - position >= Math.Max(10000, 11000 * _rate))
            return;
        if (position - next > 3500)
            next = Math.Max(0, position - 300);
        var audioTracks = _media?.Tracks.Where(info => info.TrackType == TrackType.Audio).ToArray() ?? [];
        var ordinal = Array.FindIndex(audioTracks, info => info.Id == track);
        ReadLiveWindow(_current.Location, ordinal, next, duration);
    }

    private async void ReadLiveWindow(string input, int ordinal, long next, long duration)
    {
        using var work = new CancellationTokenSource();
        _liveWork = work;
        var advance = Math.Min(duration, next + (_liveHasResults ? 8500 : 5500));
        try
        {
            var index = _liveAudioStream ?? await SpeechRecognition.FindAudioStream(input, ordinal, work.Token);
            var cues = await SpeechRecognition.RecognizeWindow(input, index, Math.Max(0, next - 1100),
                Math.Min(duration, advance + 1100), work.Token);
            if (!ReferenceEquals(_liveWork, work))
                return;
            _liveAudioStream = index;
            _liveNextWindow = advance;
            _liveHasResults = true;
            var lastEnd = _liveCues.Count == 0 ? next : Math.Max(next, _liveCues[^1].End);
            foreach (var cue in cues)
            {
                var start = Math.Max(lastEnd, cue.Start);
                if (start >= advance || cue.End - start < 200)
                    continue;
                _liveCues.Add(cue with { Start = start });
                lastEnd = cue.End;
            }
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
