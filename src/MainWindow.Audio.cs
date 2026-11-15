using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    private static readonly int[] BoostSteps = [0, 3, 6, 9];
    private static readonly AudioPreset[] AudioPresets =
    [
        new("Plano", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        new("Diálogos", [-4, -3, -2, 0, 2, 4, 4, 2, 0, -1]),
        new("Cine", [4, 3, 2, 0, 1, 2, 2, 1, 0, 0]),
        new("Música", [3, 2, 1, 0, -1, 0, 1, 2, 3, 2]),
        new("Graves", [5, 5, 3, 1, 0, 0, 0, 0, 0, 0]),
        new("Suave", [-3, -2, -1, 0, 1, 1, 0, -2, -3, -4])
    ];

    private int _audioBoost;
    private int _audioPreset;

    private void OnSoundMenu(object sender, RoutedEventArgs e) => ShowMenu((Button)sender, PlacementMode.Top,
        BuildSoundItems());

    private MenuItem BuildSoundMenu() => Submenu("Sonido", BuildSoundItems());

    private Control[] BuildSoundItems() =>
    [
        Submenu("Refuerzo de volumen", BoostSteps.Select(boost => CheckCommand(
            boost == 0 ? "Desactivado" : $"+{boost} dB", _audioBoost == boost, () => SetAudioProfile(_audioPreset, boost)))),
        Submenu("Ecualizador", AudioPresets.Select((preset, index) => CheckCommand(
            preset.Name, _audioPreset == index, () => SetAudioProfile(index, _audioBoost))))
    ];

    private void SetAudioProfile(int preset, int boost, bool announce = true)
    {
        if (!ApplyAudioProfile(preset, boost))
        {
            SetStatus("No se pudo aplicar el ajuste de audio.", error: true);
            return;
        }
        _audioPreset = preset;
        _audioBoost = boost;
        SoundButton.Foreground = (System.Windows.Media.Brush)FindResource(
            preset == 0 && boost == 0 ? "TextBrush" : "AccentBrush");
        SoundButton.ToolTip = boost == 0 ? $"Sonido · {AudioPresets[preset].Name}"
            : $"Sonido · {AudioPresets[preset].Name} · +{boost} dB";
        if (announce)
            SetStatus((string)SoundButton.ToolTip);
    }

    private bool ApplyAudioProfile(int preset, int boost)
    {
        if (_player is null)
            return false;
        if (preset == 0 && boost == 0)
            return _player.UnsetEqualizer();

        var gains = AudioPresets[preset].Gains;
        using var equalizer = new Equalizer();
        equalizer.SetPreamp(boost - gains.Max());
        for (uint band = 0; band < gains.Length; band++)
            equalizer.SetAmp(gains[band], band);
        return _player.SetEqualizer(equalizer);
    }

    private sealed record AudioPreset(string Name, float[] Gains);
}
