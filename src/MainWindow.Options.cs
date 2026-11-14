using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.Win32;

namespace LumenPlayer;

public partial class MainWindow
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("es-CL");
    private static readonly float[] Rates = [0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f];

    private static readonly ImagePreset[] Presets =
    [
        new("Original", 1, 1, 1, 1, 0),
        new("Cine", 0.98, 1.18, 0.89, 1.07, 0),
        new("Vívido", 1.02, 1.12, 1.44, 1, 0),
        new("Blanco y negro", 1, 1.17, 0, 1.03, 0),
        new("Realzar sombras", 1.08, 1, 1, 1.38, 0)
    ];

    private float _rate = 1;
    private long _subtitleDelayMs;
    private bool _applyingPreset;
    private bool _capturing;
    private string? _aspectRatio;
    private string? _deinterlace;

    private void OnOpenMenu(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
        PlacementMode.Bottom,
        Command("Abrir archivos…", () => OpenFiles(play: true), "Ctrl+O"),
        Command("Agregar a la cola…", () => OpenFiles(play: false), "Ctrl+Mayús+O"),
        MenuSeparator(),
        Command("Ver atajos de teclado", ShowShortcuts));

    private void ShowShortcuts() => MessageBox.Show(this,
        "Abrir archivos · Ctrl+O\nAgregar a la cola · Ctrl+Mayús+O\nReproducir o pausar · Espacio\n"
        + "Saltar 5 segundos · ← y →\nSilenciar · M\nPantalla completa · F o Alt+Enter\nSalir de pantalla completa · Esc\n"
        + "Cargar subtítulos · Ctrl+T\nMostrar u ocultar panel · Ctrl+L\nAjustar imagen · Ctrl+E\nGuardar fotograma · Ctrl+S",
        "Atajos de Lumen Player", MessageBoxButton.OK);

    private void OnSubtitleMenu(object sender, RoutedEventArgs e)
    {
        var enabled = _current is not null;
        var menu = new List<Control>
        {
            Command("Cargar subtítulos…", OpenSubtitle, "Ctrl+T", enabled),
            MenuSeparator()
        };

        var trackItems = BuildTracks(_player?.SpuDescription, _player?.Spu ?? -1, id =>
        {
            if (_player?.SetSpu(id) == true)
                SetStatus(id < 0 ? "Subtítulos desactivados" : "Pista de subtítulos seleccionada");
        }, "Sin pistas de subtítulos");
        menu.AddRange(trackItems);

        if (_requestedPlayback && _player?.Spu >= 0)
        {
            menu.Add(MenuSeparator());
            var delayMenu = new MenuItem { Header = $"Sincronización ({FormatDelay(_subtitleDelayMs)})" };
            delayMenu.Items.Add(Command("Adelantar 100 ms", () => SetSubtitleDelay(_subtitleDelayMs - 100)));
            delayMenu.Items.Add(Command("Atrasar 100 ms", () => SetSubtitleDelay(_subtitleDelayMs + 100)));
            delayMenu.Items.Add(Command("Restablecer a 0 ms", () => SetSubtitleDelay(0)));
            menu.Add(delayMenu);
        }
        ShowMenu((Button)sender, PlacementMode.Top, [.. menu]);
    }

    private void OpenSubtitle()
    {
        if (_current is null)
        {
            SetStatus("Abre un video para cargar subtítulos.", error: true);
            return;
        }

        var picker = new OpenFileDialog
        {
            Title = "Cargar subtítulos",
            Filter = "Subtítulos|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx;*.smi;*.ttml;*.dfxp;*.scc;*.sup;*.usf|Todos los archivos|*.*",
            Multiselect = true,
            InitialDirectory = Path.GetDirectoryName(_current.Location)
        };

        if (picker.ShowDialog(this) == true)
            AttachSubtitles(picker.FileNames);
    }

    private void AttachSubtitles(IEnumerable<string> paths)
    {
        if (_current is null || _player is null)
        {
            SetStatus("Abre un video para cargar subtítulos.", error: true);
            return;
        }

        var attached = 0;
        var failed = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || (_requestedPlayback && !_player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true)))
            {
                failed++;
                continue;
            }
            if (!_current.Subtitles.Contains(path, StringComparer.OrdinalIgnoreCase))
                _current.Subtitles.Add(path);
            attached++;
        }

        if (failed > 0)
            SetStatus(attached == 0 ? "No se pudieron cargar los subtítulos." : $"Subtítulos cargados: {attached}. Fallaron: {failed}.", error: true);
        else if (!_requestedPlayback)
            SetStatus("Los subtítulos se cargarán al reproducir.");
        else
            SetStatus(attached == 1 ? "Subtítulos cargados" : $"Archivos de subtítulos cargados: {attached}");
    }

    private void SetSubtitleDelay(long milliseconds)
    {
        if (_player?.SetSpuDelay(milliseconds * 1000) == true)
        {
            _subtitleDelayMs = milliseconds;
            SetStatus("Subtítulos: " + FormatDelay(milliseconds));
        }
    }

    private static string FormatDelay(long milliseconds) => $"{milliseconds:+0;-0;0} ms";

    private void OnPlaybackMenu(object sender, RoutedEventArgs e)
    {
        var audio = new MenuItem { Header = "Pistas de audio" };
        var tracks = BuildTracks(_player?.AudioTrackDescription, _player?.AudioTrack ?? -1, id =>
        {
            if (_player is not null && _player.SetAudioTrack(id))
                SetStatus(id < 0 ? "Audio desactivado" : "Pista de audio seleccionada");
        }, "Sin pistas de audio");
        foreach (var track in tracks)
            audio.Items.Add(track);

        var speed = new MenuItem { Header = "Velocidad" };
        foreach (var rate in Rates)
        {
            var label = rate.ToString("0.##", DisplayCulture) + "×";
            speed.Items.Add(CheckCommand(label, _rate == rate, () => SetRate(rate)));
        }

        var aspect = new MenuItem { Header = "Proporción de video" };
        aspect.Items.Add(CheckCommand("Original", _aspectRatio is null, () => SetAspect(null)));
        foreach (var ratio in new[] { "16:9", "4:3", "21:9", "1:1" })
            aspect.Items.Add(CheckCommand(ratio, _aspectRatio == ratio, () => SetAspect(ratio)));

        var deinterlace = new MenuItem { Header = "Desentrelazado" };
        deinterlace.Items.Add(CheckCommand("Desactivado", _deinterlace is null, () => SetDeinterlace(null)));
        deinterlace.Items.Add(CheckCommand("Yadif", _deinterlace == "yadif", () => SetDeinterlace("yadif")));
        deinterlace.Items.Add(CheckCommand("Yadif (doble frecuencia)", _deinterlace == "yadif2x", () => SetDeinterlace("yadif2x")));

        ShowMenu((Button)sender, PlacementMode.Top, audio, speed, aspect, deinterlace, MenuSeparator(),
            Command("Ajustes de imagen", ShowImagePanel, "Ctrl+E"),
            Command("Guardar fotograma como PNG…", SaveSnapshot, "Ctrl+S", _videoVisible));
    }

    private static IReadOnlyList<MenuItem> BuildTracks(TrackDescription[]? descriptions, int selected, Action<int> change, string empty)
    {
        if (descriptions is null || !descriptions.Any(track => track.Id >= 0))
            return [Command(empty, () => { }, enabled: false)];

        return descriptions.Select(track => CheckCommand(
            track.Id < 0 ? "Desactivar" : track.Name ?? $"Pista {track.Id}",
            selected == track.Id, () => change(track.Id))).ToArray();
    }

    private void SetRate(float rate)
    {
        if (_requestedPlayback && _streamSettingsApplied && !ApplyRate(rate))
        {
            SetStatus("Este archivo no admite esa velocidad.", error: true);
            return;
        }
        _rate = rate;
        SetStatus("Velocidad " + rate.ToString("0.##", DisplayCulture) + "×");
    }

    private bool ApplyRate(float rate) => _player?.SetRate(rate) == 0;

    private void OnMute(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        _muted = !_muted;
        ApplyVolume();
        UpdateVolumeIcon();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready)
            return;
        if (e.NewValue > 0)
            _muted = false;
        ApplyVolume();
        UpdateVolumeIcon();
    }

    private void ApplyVolume()
    {
        if (_player is null)
            return;
        _player.Volume = (int)Math.Round(VolumeSlider.Value);
        _player.Mute = _muted;
    }

    private void UpdateVolumeIcon()
    {
        VolumeIcon.Text = _muted || VolumeSlider.Value == 0 ? "\uE74F" : "\uE767";
        var label = _muted ? "Activar sonido" : "Silenciar";
        MuteButton.ToolTip = label + " (M)";
        AutomationProperties.SetName(MuteButton, label);
    }

    private void OnPresetMenu(object sender, RoutedEventArgs e) => ShowMenu((Button)sender, PlacementMode.Bottom,
        Presets.Select(preset => CheckCommand(preset.Name, Equals(PresetButton.Content, preset.Name), () => SetPreset(preset))).ToArray());

    private void OnResetEffects(object sender, RoutedEventArgs e) => SetPreset(Presets[0]);

    private void SetPreset(ImagePreset preset)
    {
        _applyingPreset = true;
        Brightness.Value = preset.Brightness;
        Contrast.Value = preset.Contrast;
        Saturation.Value = preset.Saturation;
        Gamma.Value = preset.Gamma;
        Hue.Value = preset.Hue;
        PresetButton.Content = preset.Name;
        _applyingPreset = false;
        ApplyEffects();
    }

    private void OnEffectChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _applyingPreset)
            return;
        PresetButton.Content = "Personalizado";
        ApplyEffects();
    }

    private void ApplyEffects()
    {
        if (_player is null || !_videoVisible)
            return;
        var modified = Brightness.Value != 1 || Contrast.Value != 1 || Saturation.Value != 1 || Gamma.Value != 1 || Hue.Value != 0;
        _player.SetAdjustInt(VideoAdjustOption.Enable, modified ? 1 : 0);
        if (!modified)
            return;
        _player.SetAdjustFloat(VideoAdjustOption.Brightness, (float)Brightness.Value);
        _player.SetAdjustFloat(VideoAdjustOption.Contrast, (float)Contrast.Value);
        _player.SetAdjustFloat(VideoAdjustOption.Saturation, (float)Saturation.Value);
        _player.SetAdjustFloat(VideoAdjustOption.Gamma, (float)Gamma.Value);
        _player.SetAdjustFloat(VideoAdjustOption.Hue, (float)Hue.Value);
    }

    private void ApplyVideoSettings()
    {
        if (_player is null)
            return;
        _player.AspectRatio = _aspectRatio;
        _player.SetDeinterlace(_deinterlace);
        ApplyEffects();
    }

    private void SetAspect(string? ratio)
    {
        _aspectRatio = ratio;
        if (_videoVisible && _player is not null)
            _player.AspectRatio = ratio;
    }

    private void SetDeinterlace(string? mode)
    {
        _deinterlace = mode;
        if (_videoVisible)
            _player?.SetDeinterlace(mode);
    }

    private async void SaveSnapshot()
    {
        if (_capturing)
            return;
        if (_player is null || !_videoVisible || _current is null)
        {
            SetStatus("Reproduce un video para guardar un fotograma.", error: true);
            return;
        }

        var picker = new SaveFileDialog
        {
            Title = "Guardar fotograma",
            Filter = "Imagen PNG|*.png",
            AddExtension = true,
            DefaultExt = ".png",
            FileName = Path.GetFileNameWithoutExtension(_current.Title) + "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)
        };
        if (picker.ShowDialog(this) != true)
            return;

        var target = picker.FileName;
        var pending = Path.Combine(Path.GetDirectoryName(target)!, $".lumen-{Guid.NewGuid():N}.png");
        _capturing = true;
        try
        {
            if (!_player.TakeSnapshot(0, pending, 0, 0))
                throw new IOException("LibVLC no pudo capturar este fotograma.");

            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                if (!File.Exists(pending))
                    continue;
                try
                {
                    using (new FileStream(pending, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    File.Move(pending, target, overwrite: true);
                    SetStatus("Fotograma guardado: " + Path.GetFileName(target));
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    // The video output can still be writing the PNG.
                }
            }
            throw new IOException("LibVLC no terminó de guardar la imagen.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("No se pudo guardar el fotograma.", error: true);
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
        finally
        {
            try { File.Delete(pending); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning(ex.Message);
            }
            _capturing = false;
        }
    }

    private Separator MenuSeparator() => new() { Style = (Style)FindResource("MenuSeparator") };

    private static MenuItem Command(string label, Action action, string gesture = "", bool enabled = true)
    {
        var item = new MenuItem { Header = label, InputGestureText = gesture, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CheckCommand(string label, bool selected, Action action)
    {
        var item = Command(label, action);
        item.IsCheckable = true;
        item.IsChecked = selected;
        return item;
    }

    private void ShowMenu(Button anchor, PlacementMode placement, params Control[] items)
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Custom,
            PlacementTarget = anchor,
            CustomPopupPlacementCallback = (popup, target, _) =>
            [
                new(new Point(target.Width - popup.Width, placement == PlacementMode.Top ? -popup.Height : target.Height), PopupPrimaryAxis.Horizontal),
                new(new Point(target.Width - popup.Width, placement == PlacementMode.Top ? target.Height : -popup.Height), PopupPrimaryAxis.Horizontal)
            ]
        };
        foreach (var item in items)
            menu.Items.Add(item);
        _openMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openMenu, menu))
                _openMenu = null;
        };
        menu.IsOpen = true;
    }

    private sealed record ImagePreset(string Name, double Brightness, double Contrast, double Saturation, double Gamma, double Hue);
}
