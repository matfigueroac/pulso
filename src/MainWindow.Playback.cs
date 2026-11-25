using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace LumenPlayer;

public partial class MainWindow
{
    private const string MediaFilter =
        "Videos y audio|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.mts;*.ogv;*.3gp;*.flv;*.vob;*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aiff;*.ac3|Todos los archivos|*.*";

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".smi", ".ttml", ".dfxp", ".scc", ".sup", ".usf"
    };

    private readonly PlaybackQueue<QueueEntry> _queue = new();
    private LibVLC? _engine;
    private MediaPlayer? _player;
    private Media? _media;
    private QueueEntry? _current => _queue.Current;
    private bool _requestedPlayback => _queue.Request is not null;
    private bool _syncSeekBar;
    private bool _draggingSeek;
    private bool _videoVisible;
    private bool _streamSettingsApplied;
    private bool _preferHardwareDecoding;
    private VLCState _lastState = VLCState.NothingSpecial;


    private bool InitializePlayback()
    {
        try
        {
            Core.Initialize();
            _engine = new LibVLC([.. PlaybackEngineOptions(), "--equalizer-2pass"]);
            _player = new MediaPlayer(_engine)
            {
                EnableKeyInput = false,
                EnableMouseInput = false
            };
            Video.MediaPlayer = _player;
            return true;
        }
        catch (Exception ex)
        {
            DisposePlayback();
            SetPlaceholder("Motor de reproducción no disponible", "Abre Pulso desde su carpeta completa.", allowAction: false);
            SetStatus("No se pudo iniciar LibVLC.", error: true);
            System.Diagnostics.Trace.TraceError(ex.ToString());
            return false;
        }
    }

    private void CancelPlaybackRequest()
    {
        DetachPlaybackEvents();
        if (_current is { } entry)
            entry.PlaybackLabel = null;
        _queue.Stop();
        UpdateVideoEffectAvailability(hardwareDecoding: false);
    }

    private void DetachPlaybackEvents()
    {
        if (_player is not null && _queue.Request is { } request)
        {
            _player.Playing -= request.OnPlaying;
            _player.EncounteredError -= request.OnError;
        }
    }

    private void DisposePlayback()
    {
        CancelPlaybackRequest();
        ClearSrtSelection();
        _player?.Stop();
        Video.MediaPlayer = null;
        _player?.Dispose();
        _media?.Dispose();
        _engine?.Dispose();
        _player = null;
        _media = null;
        _engine = null;
    }

    private void OnAdd(object sender, RoutedEventArgs e) => OpenFiles(play: false);

    private void OnHero(object sender, RoutedEventArgs e)
    {
        if (_current is not null && !_requestedPlayback)
            PlayPause();
        else
            OpenFiles(play: true);
    }

    private void OpenFiles(bool play)
    {
        if (_engine is null)
            return;
        var picker = new OpenFileDialog
        {
            Title = play ? "Abrir archivos" : "Agregar archivos a la cola",
            Filter = MediaFilter,
            Multiselect = true
        };
        if (picker.ShowDialog(this) == true)
            AddPaths(picker.FileNames, play);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _engine is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            AddPaths(paths, play: !ReferenceEquals(sender, QueuePanel));
        e.Handled = true;
    }

    private void AddPaths(IEnumerable<string> paths, bool play)
    {
        var files = paths.Where(File.Exists).Select(Path.GetFullPath).ToArray();
        var entries = files.Where(path => !SubtitleExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new QueueEntry(path, Path.GetFileName(path), Path.GetExtension(path).ToUpperInvariant().TrimStart('.')))
            .ToArray();
        var subtitles = files.Except(entries.Select(entry => entry.Location), StringComparer.OrdinalIgnoreCase).ToArray();

        if (entries.Length > 0)
        {
            foreach (var entry in entries)
                _queue.Items.Add(entry);

            var mediaPaths = entries.Select(entry => entry.Location).ToArray();
            foreach (var subtitle in subtitles)
            {
                var owner = entries[SubtitlePairing.FindOwner(subtitle, mediaPaths)];
                owner.Subtitles.Add(subtitle);
                owner.HasExplicitSubtitles = true;
            }
            QueueList.SelectedItem = entries[0];
            QueueList.ScrollIntoView(entries[0]);
            UpdateQueue();
            if (play)
                PlayItem(entries[0]);
        }

        if (entries.Length == 0)
        {
            if (subtitles.Length > 0)
                AttachSubtitles(subtitles);
            else
                SetStatus("No se encontraron archivos para abrir.", error: true);
        }
    }

    private void PlayItem(QueueEntry entry)
    {
        if (_engine is null || _player is null)
            return;

        SavePlaybackBookmark();
        CancelPlaybackRequest();
        ClearSrtSelection();
        ResetLiveWindow();
        _previousSubtitleTrack = null;
        if (_media is not null)
        {
            _player.Stop();
            _player.Media = null;
            _media.Dispose();
            _media = null;
        }

        FlushSubtitleAppearance();
        var request = _queue.Begin(entry);
        QueueList.SelectedItem = entry;
        QueueList.ScrollIntoView(entry);
        NowPlaying.Text = entry.Title;
        NowPlaying.ToolTip = entry.Location;
        Title = entry.Title + " · Pulso";
        _streamSettingsApplied = false;
        _lastState = VLCState.NothingSpecial;
        PrepareSubtitleSync(entry);
        PreparePlaybackBookmark(entry);
        LoadSubtitleAppearance();
        SetVideoVisible(false);
        SetPlaceholder("Abriendo archivo…", entry.Title, allowAction: false);
        SetStatus("Cargando…");
        SyncSeek(0, 0, false);
        UpdateQueue();

        if (!File.Exists(entry.Location))
        {
            PlaybackFailed("El archivo ya no está en esa ubicación.");
            return;
        }

        _media = new Media(_engine, entry.Location, FromType.FromPath);
        _player.EnableHardwareDecoding = _preferHardwareDecoding;
        UpdateVideoEffectAvailability(_preferHardwareDecoding);
        if (_preferHardwareDecoding)
            _player.SetAdjustInt(VideoAdjustOption.Enable, 0);
        ApplyBookmarkStartTime(_media);
        PrepareStartupSubtitles(entry);

        // Give the embedded HWND its layout before the decoder produces the first frame.
        _player.Playing += request.OnPlaying;
        _player.EncounteredError += request.OnError;
        UpdatePlayButton();
        UpdateNavigation();
        Video.Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() =>
        {
            if (!_queue.CanStart(request))
                return;
            request.Started = true;
            if (!_player.Play(_media))
                PlaybackFailed("LibVLC no pudo abrir este archivo.");
        }, DispatcherPriority.Background);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => PlayPause();

    private void PlayPause()
    {
        if (_player is null)
            return;
        if (_requestedPlayback && _player.State == VLCState.Playing)
            _player.SetPause(true);
        else if (_requestedPlayback && _player.State == VLCState.Paused)
            _player.SetPause(false);
        else if (!_requestedPlayback)
        {
            var entry = QueueList.SelectedItem as QueueEntry ?? _current ?? _queue.Items.FirstOrDefault();
            if (entry is null)
                OpenFiles(play: true);
            else
                PlayItem(entry);
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => StopPlayback("Reproducción detenida");

    private void StopPlayback(string message, bool announce = true)
    {
        FlushSubtitleAppearance();
        SavePlaybackBookmark();
        CancelPlaybackRequest();
        ClearSrtSelection();
        ResetLiveWindow();
        _previousSubtitleTrack = null;
        _player?.Stop();
        SetVideoVisible(false);
        SetPlaceholder(message, _current?.Title ?? "", allowAction: _current is not null);
        SetStatus(message == "Reproducción detenida" ? "Detenido" : message, activity: announce ? "Playback" : null);
        SyncSeek(0, 0, false);
        UpdatePlayButton();
        UpdateNavigation();
    }

    private void PlaybackFailed(string message)
    {
        if (_queue.Request is { } request)
            request.Failed = true;
        StopPlayback("No se pudo reproducir", announce: false);
        SetStatus(message, error: true);
        HeroDetail.Text = message;
    }

    private void RefreshPlayback()
    {
        if (_player is null || _queue.Request is not { Started: true } request)
            return;

        var state = _player.State;
        if (request.Failed || state == VLCState.Error)
        {
            PlaybackFailed("No se pudo leer el archivo. Revisa su formato y ubicación.");
            return;
        }
        if (state == VLCState.Ended)
        {
            if (!CompletedPlayback(request))
            {
                PlaybackFailed("No se pudo leer el archivo. Revisa su formato y ubicación.");
                return;
            }
            if (_queue.AfterEnd() is { } next)
                PlayItem(next);
            else
                StopPlayback("Fin de la reproducción");
            return;
        }
        if (state == VLCState.NothingSpecial)
            return;

        var hasVideo = _player.VoutCount > 0;
        if (hasVideo && !_videoVisible)
        {
            SetVideoVisible(true);
            ApplyVideoSettings();
        }
        else if (!hasVideo && (_videoVisible || _lastState != state) && state is (VLCState.Playing or VLCState.Paused))
        {
            SetVideoVisible(false);
            SetPlaceholder("Reproducción de audio", _current?.Title ?? "", allowAction: false);
        }

        if (state == VLCState.Playing && !_streamSettingsApplied)
        {
            _streamSettingsApplied = true;
            ApplyVolume();
            ApplyRate(_rate);
        }
        if (state is VLCState.Playing or VLCState.Paused)
        {
            RefreshSubtitleSync();
            RefreshPlaybackBookmark(state);
            UpdateSrtSelection();
        }
        if (state != _lastState)
        {
            _lastState = state;
            if (state is VLCState.Playing or VLCState.Paused)
            {
                var label = state == VLCState.Paused ? "En pausa" : "Reproduciendo";
                if (_current is { } entry)
                    entry.PlaybackLabel = label;
                SetStatus(state == VLCState.Paused ? label : $"{label} · {_current?.Title}", activity: "Playback");
            }
            UpdatePlayButton();
        }

        var duration = Math.Max(0, _player.Length);
        SyncSeek(Math.Max(0, _player.Time), duration, duration > 0 && _player.IsSeekable);
    }

    // VLC can briefly report Playing even when the demuxer found no playable stream.
    private bool CompletedPlayback(PlaybackRequest request) =>
        request.Played && !request.Failed && (_player?.Length > 0 || _media?.Tracks.Any(
            track => track.TrackType is TrackType.Audio or TrackType.Video) == true);

    private void SetVideoVisible(bool visible)
    {
        _videoVisible = visible;
        if (!visible)
            UpdateAspectLabel();
        Video.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateTransportHost();
        UpdateViewportLayout();
    }

    private void SetPlaceholder(string heading, string detail, bool allowAction)
    {
        HeroTitle.Text = heading;
        HeroDetail.Text = detail;
        HeroButton.Content = _current is null ? "Abrir archivos" : "Reproducir";
        HeroButton.Visibility = allowAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string text, bool error = false, string? activity = null)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        StatusText.Foreground = (Brush)FindResource(error ? "ErrorBrush" : "MutedBrush");
        if ((error || activity is not null) && AutomationPeer.ListenerExists(AutomationEvents.Notification))
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(this);
            peer?.RaiseNotificationEvent(error ? AutomationNotificationKind.ActionAborted : AutomationNotificationKind.ActionCompleted,
                error ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.MostRecent,
                text, error ? "Error" : activity!);
        }
    }

    private void UpdatePlayButton()
    {
        var playing = _requestedPlayback && _player?.State == VLCState.Playing;
        PlayIcon.Text = playing ? "\uE769" : "\uE768";
        var action = playing ? "Pausar" : "Reproducir";
        PlayButton.ToolTip = action + " (Espacio)";
        AutomationProperties.SetName(PlayButton, action);
        StopButton.IsEnabled = _requestedPlayback;
    }

    private void OnNext(object sender, RoutedEventArgs e) => PlayRelative(1);

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (_requestedPlayback && _player is { IsSeekable: true, Time: > 3_000 })
            SeekTo(0);
        else
            PlayRelative(-1);
    }

    private bool PlayRelative(int offset)
    {
        if (_queue.Relative(offset) is not { } next)
            return false;
        PlayItem(next);
        return true;
    }

    private void OnQueueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(QueueList, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: QueueEntry entry })
            PlayItem(entry);
    }

    private void OnQueueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && QueueList.SelectedItem is QueueEntry entry)
            PlayItem(entry);
        else if (e.Key == Key.Delete)
            RemoveSelected();
        else
            return;
        e.Handled = true;
    }

    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoveButton is not null)
            RemoveButton.IsEnabled = QueueList.SelectedIndex >= 0;
    }

    private void OnRemove(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (QueueList.SelectedItem is not QueueEntry entry)
            return;

        if (ReferenceEquals(entry, _current))
        {
            SavePlaybackBookmark();
            FlushSubtitleAppearance();
            DetachPlaybackEvents();
            entry.PlaybackLabel = null;
        }
        var removal = _queue.Remove(entry);
        if (removal.RemovedCurrent)
        {
            if (removal.Replacement is { } next)
                PlayItem(next);
            else
            {
                StopPlayback("Sin reproducción");
                LoadSubtitleAppearance();
                Title = "Pulso";
                NowPlaying.Text = "Sin reproducción";
                NowPlaying.ToolTip = null;
                SetPlaceholder("Arrastra un archivo aquí", "Reproduce video o audio desde tu equipo.", allowAction: true);
                SetStatus("");
            }
        }
        if (_queue.Items.Count > 0 && QueueList.SelectedIndex < 0)
            QueueList.SelectedIndex = removal.Selection;
        UpdateQueue();
    }

    private void OnRepeat(object sender, RoutedEventArgs e)
    {
        _queue.CycleRepeat();
        var label = _queue.Repeat switch
        {
            RepeatMode.All => "Repetir toda la cola",
            RepeatMode.Current => "Repetir archivo actual",
            _ => "Repetición desactivada"
        };
        RepeatButton.Foreground = (Brush)FindResource(_queue.Repeat == RepeatMode.Off ? "MutedBrush" : "AccentBrush");
        RepeatIcon.Text = _queue.Repeat == RepeatMode.Current ? "\uE8ED" : "\uE8EE";
        RepeatButton.ToolTip = label;
        AutomationProperties.SetName(RepeatButton, label);
        UpdateQueue();
    }

    private void UpdateQueue()
    {
        var count = _queue.Items.Count;
        QueueCount.Text = count == 1 ? "1 archivo" : $"{count} archivos";
        QueueList.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyQueue.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var count = _queue.Items.Count;
        var index = _current is null ? -1 : _queue.Items.IndexOf(_current);
        PreviousButton.IsEnabled = index >= 0 && (index > 0 || _requestedPlayback || _queue.Repeat == RepeatMode.All);
        NextButton.IsEnabled = count > 0 && (index < count - 1 || _queue.Repeat == RepeatMode.All);
    }

    private void SyncSeek(long time, long duration, bool canSeek)
    {
        if (!_draggingSeek)
        {
            _syncSeekBar = true;
            SeekBar.Value = duration > 0 ? Math.Clamp((double)time / duration, 0, 1) : 0;
            _syncSeekBar = false;
            ElapsedText.Text = FormatTime(time);
        }

        SeekBar.IsEnabled = canSeek;
        DurationText.Text = duration > 0 ? FormatTime(duration) : "--:--";
    }

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _draggingSeek = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingSeek = false;
        if (!e.Canceled)
            SeekToFraction(SeekBar.Value);
    }

    private void OnSeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncSeekBar || _player is not { IsSeekable: true, Length: > 0 })
            return;

        if (_draggingSeek)
            ElapsedText.Text = FormatTime((long)(_player.Length * e.NewValue));
        else
            SeekToFraction(e.NewValue);
    }

    private void SeekToFraction(double fraction)
    {
        if (_player is { IsSeekable: true, Length: > 0 })
            SeekTo((long)(_player.Length * fraction));
    }

    private void Skip(long milliseconds)
    {
        if (_player is { IsSeekable: true, Length: > 0 })
            SeekTo(Math.Clamp(_player.Time + milliseconds, 0, _player.Length));
    }

    private void SeekTo(long position)
    {
        if (_player is not { IsSeekable: true })
            return;
        if (_liveCaptions)
            ResetLiveWindow();
        _resume.Seek(position, Environment.TickCount64);
        _player.Time = position;
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }


    private sealed class QueueEntry(string location, string title, string format) : INotifyPropertyChanged
    {
        private string? _playbackLabel;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Location { get; } = location;
        public string DisplayLocation => Location;
        public string Title { get; } = title;
        public string Format { get; } = format;
        public string Icon => "\uE714";
        public List<string> Subtitles { get; } = [];
        public SubtitleSyncStore SubtitleSync { get; } = new(location);
        public bool HasExplicitSubtitles { get; set; }

        public string AccessibleName => PlaybackLabel is { } label ? $"{label}. {Title}" : Title;

        public string? PlaybackLabel
        {
            get => _playbackLabel;
            set
            {
                if (_playbackLabel == value)
                    return;
                _playbackLabel = value;
                PropertyChanged?.Invoke(this, new(nameof(PlaybackLabel)));
                PropertyChanged?.Invoke(this, new(nameof(AccessibleName)));
            }
        }

        public bool TakeExplicitSubtitles()
        {
            var explicitSubtitles = HasExplicitSubtitles;
            HasExplicitSubtitles = false;
            return explicitSubtitles;
        }
    }
}
