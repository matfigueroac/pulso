using System;
using System.Windows;
using System.Windows.Media;

namespace LumenPlayer;

internal sealed record SubtitleAppearance(int Size = 22, int Color = 0xFFFFFF,
    int TextOpacity = 100, int BackgroundOpacity = 85, int VerticalOffset = 0,
    int OutlineWidth = 0, int ShadowOpacity = 0)
{
    internal static readonly SubtitleAppearance Default = new();

    internal SubtitleAppearance Normalized() => this with
    {
        Size = Math.Clamp(Size, 16, 38),
        Color = Math.Clamp(Color, 0, 0xFFFFFF),
        TextOpacity = Math.Clamp(TextOpacity, 10, 100),
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100),
        VerticalOffset = Math.Clamp(VerticalOffset, 0, 400),
        OutlineWidth = Math.Clamp(OutlineWidth, 0, 5),
        ShadowOpacity = Math.Clamp(ShadowOpacity, 0, 100)
    };

    internal SolidColorBrush TextBrush => new(System.Windows.Media.Color.FromRgb(
        (byte)(Color >> 16), (byte)(Color >> 8), (byte)Color)) { Opacity = TextOpacity / 100d };

    internal SolidColorBrush BackgroundBrush => new(Colors.Black) { Opacity = BackgroundOpacity / 100d };
}

public partial class MainWindow
{
    private const string SubtitleAppearanceKey = "subtitle-appearance";
    private SubtitleAppearance _defaultSubtitleAppearance = SubtitleAppearance.Default;
    private bool _defaultSubtitleAppearanceDirty;
    private SubtitleAppearance _subtitleAppearance = SubtitleAppearance.Default;
    private SubtitleSettingsPanel? _subtitleSettings;
    private long? _appearanceSaveAt;

    private void ShowSubtitleSettings() => ShowSidePanel(SubtitleTab);

    private void EnsureSubtitleSettings()
    {
        if (_subtitleSettings is null)
        {
            _subtitleSettings = new SubtitleSettingsPanel(_subtitleAppearance, UpdateSubtitleAppearance, ChangeSubtitleDelay,
                () => SetSubtitleDelay(0), SaveDefaultSubtitleAppearance, UseDefaultSubtitleAppearance);
            SubtitlePanel.Content = _subtitleSettings;
        }
        RefreshSubtitleSettings();
    }

    private void RefreshSubtitleSettings()
    {
        if (_subtitleSettings is null)
            return;

        var track = _liveCaptions ? "CC en vivo" : _displayedSrt is not null ? "Subtítulos SRT" : "Pista seleccionada";
        var styleNote = _current is null
            ? "Estilo predeterminado para los videos sin un ajuste guardado."
            : !_liveCaptions && _displayedSrt is null && CurrentSubtitleTrack >= 0
                ? "Esta pista usa el estilo predeterminado. Los ajustes de este video se aplican a los SRT y los CC."
                : "El estilo de los SRT y los CC se guarda para este video.";
        _subtitleSettings.RefreshSync(CanAdjustSubtitleDelay, track, FormatDelay(ActiveSubtitleDelay), styleNote);
        _subtitleSettings.SetCanUseDefault(_current is not null);
    }

    private void UpdateSubtitleAppearance(SubtitleAppearance appearance)
    {
        if (_current is null)
        {
            _defaultSubtitleAppearance = appearance;
            _defaultSubtitleAppearanceDirty = true;
        }
        else
            _appearanceSaveAt = Environment.TickCount64 + 350;

        _subtitleAppearance = appearance;
        ApplyCaptionStyle();
    }

    private void FlushSubtitleAppearance()
    {
        if (_appearanceSaveAt is null)
            return;
        _appearanceSaveAt = null;
        if (_current?.SubtitleSync.Save(SubtitleAppearanceKey, _subtitleAppearance) != true)
            SetStatus("No se pudo guardar el estilo de subtítulos de este video.", error: true);
    }

    private string SaveDefaultSubtitleAppearance()
    {
        if (!SavePreferences(ReadPreferences() with { SubtitleStyle = _subtitleAppearance }))
            return "No se pudo guardar el estilo predeterminado.";

        _defaultSubtitleAppearance = _subtitleAppearance;
        _defaultSubtitleAppearanceDirty = false;
        return "Estilo guardado como predeterminado.";
    }

    private string UseDefaultSubtitleAppearance()
    {
        if (_current?.SubtitleSync.Clear(SubtitleAppearanceKey) != true)
            return "No se pudo restablecer el estilo de este video.";

        _appearanceSaveAt = null;
        LoadSubtitleAppearance();
        return "Este video seguirá el estilo predeterminado.";
    }

    private void LoadSubtitleAppearance()
    {
        _subtitleAppearance = _current?.SubtitleSync.Read<SubtitleAppearance>(SubtitleAppearanceKey)?.Normalized()
            ?? _defaultSubtitleAppearance;
        ApplyCaptionStyle();
        _subtitleSettings?.LoadAppearance(_subtitleAppearance);
        RefreshSubtitleSettings();
    }

    private void ApplyCaptionStyle()
    {
        LiveCaptionText.ApplyAppearance(_subtitleAppearance);
        LiveCaptionBox.Background = _subtitleAppearance.BackgroundBrush;
        UpdateCaptionPosition();
    }

    private void UpdateCaptionPosition()
    {
        var available = Math.Max(0, Video.ActualHeight - _subtitleAppearance.Size * 3 - 40);
        LiveCaptionBox.Margin = new Thickness(0, 0, 0, Math.Min(_subtitleAppearance.VerticalOffset, available));
    }

    private string[] PlaybackEngineOptions()
    {
        var appearance = _subtitleAppearance;
        return
        [
            "--no-video-title-show", "--no-osd",
            FormattableString.Invariant($"--freetype-rel-fontsize={Math.Round(16d * 22 / appearance.Size):0}"),
            FormattableString.Invariant($"--freetype-color={appearance.Color}"),
            FormattableString.Invariant($"--freetype-opacity={Math.Round(appearance.TextOpacity * 255d / 100):0}"),
            FormattableString.Invariant($"--freetype-background-opacity={Math.Round(appearance.BackgroundOpacity * 255d / 100):0}"),
            FormattableString.Invariant($"--sub-margin={appearance.VerticalOffset}")
        ];
    }
}
