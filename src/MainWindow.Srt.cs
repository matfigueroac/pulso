using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private const uint VlcTextCodec = 0x74627573; // VLC's 'subt' FourCC.
    private readonly Dictionary<int, SrtTimeline> _srtTracks = [];
    private CancellationTokenSource? _srtLoad;
    private (int Id, SrtTimeline Timeline)? _displayedSrt;
    private int? _srtAttemptedTrack;

    private int CurrentSubtitleTrack => _displayedSrt?.Id ?? _player?.Spu ?? -1;

    private bool SelectSubtitleTrack(int id)
    {
        if (_displayedSrt?.Id == id)
            return true;
        if (_player?.SetSpu(id) != true)
            return false;

        ClearSrtSelection();
        _appliedSubtitleKey = null;
        return true;
    }

    private void ClearSrtSelection()
    {
        _srtLoad?.Cancel();
        _srtLoad = null;
        _srtAttemptedTrack = null;
        if (_displayedSrt is not null)
        {
            _displayedSrt = null;
            if (!_liveCaptions)
                ShowSubtitleCaption(null);
        }
        if (!_liveCaptions)
            _captionClock.Stop();
    }

    private async void UpdateSrtSelection()
    {
        if (_liveCaptions || !_videoVisible || _current is null || _player is null)
            return;
        if (_displayedSrt is not null && _player.Spu < 0)
            return;

        var id = _player.Spu;
        if (id == _srtAttemptedTrack || (id < 0 && _srtAttemptedTrack is null && _displayedSrt is null))
            return;
        ClearSrtSelection();
        if (id < 0)
            return;

        if (_srtTracks.TryGetValue(id, out var cached))
        {
            ShowSrtTrack(id, cached);
            return;
        }

        _subtitleFiles.TryGetValue(id, out var subtitle);
        var embedded = Array.Empty<MediaTrack>();
        var ordinal = -1;
        if (subtitle is not null)
        {
            if (!Path.GetExtension(subtitle).Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                _srtAttemptedTrack = id;
                return;
            }
        }
        else if (_media is not null)
        {
            embedded = _media.Tracks.Where(track => track.TrackType == TrackType.Text
                && !_subtitleFiles.ContainsKey(track.Id)).ToArray();
            ordinal = Array.FindIndex(embedded, track => track.Id == id);
            if (ordinal < 0)
                return;
            if (embedded[ordinal].Codec != VlcTextCodec)
            {
                _srtAttemptedTrack = id;
                return;
            }
        }
        else
            return;

        _srtAttemptedTrack = id;
        using var load = new CancellationTokenSource();
        _srtLoad = load;
        try
        {
            var timeline = subtitle is not null
                ? await SrtTimeline.ReadFile(subtitle, load.Token)
                : await SpeechRecognition.ReadEmbeddedSrt(_current.Location, ordinal, embedded.Length, load.Token);
            if (load.IsCancellationRequested || _liveCaptions || !_requestedPlayback || _player?.Spu != id
                || timeline is null)
                return;

            _srtTracks[id] = timeline;
            ShowSrtTrack(id, timeline);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or System.Text.Json.JsonException or InvalidOperationException)
        {
            Trace.TraceWarning("Pulso: no se pudo preparar la pista SRT. {0}", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_srtLoad, load))
                _srtLoad = null;
        }
    }

    private void ShowSrtTrack(int id, SrtTimeline timeline)
    {
        if (_player?.SetSpu(-1) != true)
            return;

        _displayedSrt = (id, timeline);
        ApplySubtitleSync();
        _captionClock.Start();
        RefreshSrtCaption();
    }

    private void RefreshSrtCaption()
    {
        if (_liveCaptions || _displayedSrt is not { } track)
            return;

        var caption = !_draggingSeek && _videoVisible && _requestedPlayback
            && _player is { State: VLCState.Playing or VLCState.Paused, Time: >= 0 and var time }
            ? track.Timeline.TextAt(time - _subtitleDelayMs) : null;
        ShowSubtitleCaption(caption);
    }

    private void ShowSubtitleCaption(string? caption)
    {
        var text = caption ?? "";
        if (LiveCaptionText.Text != text)
            LiveCaptionText.Text = text;
        LiveCaptionBox.Visibility = caption is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
}
