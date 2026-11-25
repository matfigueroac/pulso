using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LumenPlayer;

public sealed class CaptionText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text),
        typeof(string), typeof(CaptionText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsRender, (sender, args) => AutomationProperties.SetName(sender, (string)args.NewValue)));

    private SubtitleAppearance _appearance = SubtitleAppearance.Default;
    private Brush _foreground = Brushes.White;
    private Pen? _outline;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    internal void ApplyAppearance(SubtitleAppearance appearance)
    {
        var sizeChanged = _appearance.Size != appearance.Size;
        _appearance = appearance;
        _foreground = appearance.TextBrush;
        _outline = appearance.OutlineWidth == 0 ? null : new Pen(
            new SolidColorBrush(Colors.Black) { Opacity = appearance.TextOpacity / 100d },
            appearance.OutlineWidth * 2) { LineJoin = PenLineJoin.Round };
        Effect = appearance.ShadowOpacity == 0 ? null : new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = appearance.ShadowOpacity / 100d,
            ShadowDepth = 3,
            BlurRadius = 5
        };
        if (sizeChanged)
            InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return new Size(0, 0);

        var text = Format(availableSize.Width);
        return new Size(Math.Ceiling(text.Width), Math.Ceiling(text.Height));
    }

    protected override void OnRender(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || RenderSize.Width <= 0)
            return;

        var text = Format(RenderSize.Width);
        var origin = new Point();
        if (_outline is not null)
        {
            var glyphs = text.BuildGeometry(origin);
            var stroke = glyphs.GetWidenedPathGeometry(_outline);
            var outline = Geometry.Combine(stroke, glyphs, GeometryCombineMode.Exclude, null);
            context.DrawGeometry(_outline.Brush, null, outline);
        }
        context.DrawText(text, origin);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private FormattedText Format(double width)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection, typeface,
            _appearance.Size, _foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            LineHeight = Math.Ceiling(_appearance.Size * 1.27),
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.None
        };
        if (double.IsFinite(width))
            text.MaxTextWidth = Math.Max(1, width);
        return text;
    }
}
