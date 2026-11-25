using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace LumenPlayer;

public partial class SubtitleSettingsPanel : UserControl
{
    private readonly Action<SubtitleAppearance> _onAppearance;
    private readonly Action<long> _changeTiming;
    private readonly Action _resetTiming;
    private readonly Func<string> _saveDefault;
    private readonly Func<string> _useDefault;
    private SubtitleAppearance _appearance;
    private string? _appearanceStatus;
    private bool _syncing;

    internal SubtitleSettingsPanel(SubtitleAppearance appearance, Action<SubtitleAppearance> onAppearance,
        Action<long> changeTiming, Action resetTiming, Func<string> saveDefault, Func<string> useDefault)
    {
        _appearance = appearance;
        _onAppearance = onAppearance;
        _changeTiming = changeTiming;
        _resetTiming = resetTiming;
        _saveDefault = saveDefault;
        _useDefault = useDefault;
        _syncing = true;
        InitializeComponent();
        LoadAppearance(appearance);
    }

    internal void RefreshSync(bool canAdjust, string track, string offset, string styleNote)
    {
        TimingControls.IsEnabled = canAdjust;
        Timing.Text = canAdjust ? $"{track}   ·   {offset}" : "Activa CC o selecciona una pista para ajustar el desfase.";
        StyleNote.Text = _appearanceStatus ?? styleNote;
    }

    internal void SetCanUseDefault(bool canUse) => UseDefaultButton.IsEnabled = canUse;

    internal void LoadAppearance(SubtitleAppearance appearance)
    {
        _syncing = true;
        _appearanceStatus = null;
        _appearance = appearance;
        TextSize.Value = appearance.Size;
        TextOpacity.Value = appearance.TextOpacity;
        BackgroundOpacity.Value = appearance.BackgroundOpacity;
        VerticalOffset.Value = appearance.VerticalOffset;
        OutlineWidth.Value = appearance.OutlineWidth;
        ShadowOpacity.Value = appearance.ShadowOpacity;
        var swatch = appearance.Color switch
        {
            0xFFE58A => Yellow,
            0xABDEFF => Blue,
            0xBDEBA6 => Green,
            _ => White
        };
        swatch.IsChecked = true;
        _syncing = false;
        UpdateSample();
    }

    private void OnStyleSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing)
            return;

        _appearance = _appearance with
        {
            Size = (int)TextSize.Value,
            TextOpacity = (int)TextOpacity.Value,
            BackgroundOpacity = (int)BackgroundOpacity.Value,
            VerticalOffset = (int)VerticalOffset.Value,
            OutlineWidth = (int)OutlineWidth.Value,
            ShadowOpacity = (int)ShadowOpacity.Value
        };
        CommitAppearance();
    }

    private void OnColorChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;

        var swatch = (RadioButton)sender;
        _appearance = _appearance with { Color = int.Parse((string)swatch.Tag, NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
        CommitAppearance();
    }

    private void CommitAppearance()
    {
        _appearanceStatus = null;
        UpdateSample();
        _onAppearance(_appearance);
    }

    private void UpdateSample()
    {
        SampleText.ApplyAppearance(_appearance);
        SampleBackground.Background = _appearance.BackgroundBrush;
        SampleBackground.Margin = new Thickness(0, 0, 0, 5 + _appearance.VerticalOffset * 0.08);
    }

    private void OnResetAppearance(object sender, RoutedEventArgs e)
    {
        LoadAppearance(SubtitleAppearance.Default);
        _onAppearance(_appearance);
    }

    private void OnSaveDefault(object sender, RoutedEventArgs e) =>
        StyleNote.Text = _appearanceStatus = _saveDefault();

    private void OnUseDefault(object sender, RoutedEventArgs e) =>
        StyleNote.Text = _appearanceStatus = _useDefault();

    private void OnAdvance(object sender, RoutedEventArgs e) => _changeTiming(-100);
    private void OnDelay(object sender, RoutedEventArgs e) => _changeTiming(100);
    private void OnResetTiming(object sender, RoutedEventArgs e) => _resetTiming();
}
