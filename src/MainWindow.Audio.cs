using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;

namespace LumenPlayer;

public partial class MainWindow
{
    // LibVLC scales the input by 0.25 per pass, about -12.04 dB.
    private const float EqualizerPassCompensationDb = 12.0412f;
    private static readonly int[] BoostSteps = [0, 3, 6, 9];
    // Match the perceived level of Plano approximately using pink noise as a reference.
    private static readonly AudioPreset[] AudioPresets =
    [
        new("Plano", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 0f),
        new("Diálogos", [-4, -3, -2, 0, 2, 4, 4, 2, 0, -1], -2f),
        new("Cine", [4, 3, 2, 0, 1, 2, 2, 1, 0, 0], -1.4f),
        new("Música", [3, 2, 1, 0, -1, 0, 1, 2, 3, 2], -1.8f),
        new("Graves", [5, 5, 3, 1, 0, 0, 0, 0, 0, 0], -1f),
        new("Suave", [-3, -2, -1, 0, 1, 1, 0, -2, -3, -4], 1.3f)
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

        var profile = AudioPresets[preset];
        using var equalizer = new Equalizer();
        // Split gains across both passes so +9 dB stays within LibVLC's +20 dB preamp limit.
        equalizer.SetPreamp(EqualizerPassCompensationDb + (boost + profile.LevelOffsetDb) / 2f);
        for (uint band = 0; band < profile.Gains.Length; band++)
            equalizer.SetAmp(profile.Gains[band] / 2f, band);
        return _player.SetEqualizer(equalizer);
    }

    private sealed record AudioPreset(string Name, float[] Gains, float LevelOffsetDb);
}
