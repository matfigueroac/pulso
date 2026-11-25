using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LumenPlayer;

public partial class MainWindow : Window
{
    private const long FullscreenBarDelay = 2_500;
    private const long FullscreenBarLeaveDelay = 420;
    private const double FullscreenBarRevealHeight = 18;
    private const double FullscreenBarSlideMilliseconds = 320;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _fullscreenPointer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _ready;
    private bool _fullscreen;
    private bool _autoHideFullscreenBar = true;
    private long _fullscreenBarVisibleUntil;
    private bool _panelVisible;
    private bool _fullscreenBarShown = true;
    private System.Drawing.Point? _keyboardBarPointer;
    private string _infoPanelTitle = "";
    private bool _muted;
    private WindowState _windowedState;
    private Rect _windowedBounds;
    private WindowChrome? _windowedChrome;
    private ContextMenu? _openMenu;

    public MainWindow()
    {
        InitializeComponent();
        SubtitlePanel.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => FlushSubtitleAppearance()), true);
        UpdatePanelVisibility(animate: false);
        _fullscreenPointer.Tick += (_, _) => UpdateFullscreenBar();
        Transport.SizeChanged += (_, _) => PositionTransport(animate: _fullscreen && TransportSlide.HasAnimatedProperties);
        QueueList.ItemsSource = _queue.Items;
        _effectTimer.Tick += OnEffectTick;
        _captionClock.Tick += (_, _) =>
        {
            RefreshLiveCaptions();
            RefreshSrtCaption();
        };
        _clock.Tick += (_, _) =>
        {
            RefreshPlayback();
            if (_appearanceSaveAt is long deadline && Environment.TickCount64 >= deadline)
                FlushSubtitleAppearance();
            if (_panelVisible && SubtitleTab.IsChecked == true)
                RefreshSubtitleSettings();
        };
        Activated += (_, _) => { if (_fullscreen) Topmost = true; };
        Deactivated += (_, _) => { if (_fullscreen) Topmost = false; };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var preferences = ReadPreferences();
        VolumeSlider.Value = Math.Clamp(preferences.Volume, 0, 100);
        _muted = preferences.Muted;
        _autoHideFullscreenBar = preferences.AutoHideFullscreenBar;
        _videoFraming = preferences.Framing;
        _preferHardwareDecoding = preferences.HardwareDecoding;
        UpdateAspectLabel();
        _defaultSubtitleAppearance = preferences.SubtitleStyle?.Normalized() ?? SubtitleAppearance.Default;
        LoadSubtitleAppearance();
        UpdateVolumeIcon();

        if (!InitializePlayback())
            return;

        SetAudioProfile(Math.Clamp(preferences.AudioPreset, 0, AudioPresets.Length - 1),
            Array.IndexOf(BoostSteps, preferences.AudioBoost) >= 0 ? preferences.AudioBoost : 0, announce: false);
        _ready = true;
        _clock.Start();
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (arguments.Length > 0)
            AddPaths(arguments, play: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        FlushSubtitleAppearance();
        SavePlaybackBookmark();
        _clock.Stop();
        _effectTimer.Stop();
        _captionClock.Stop();
        _fullscreenPointer.Stop();
        ResetLiveWindow();
        _subtitleGeneration?.Cancel();
        if (_ready)
            SavePreferences(new((int)Math.Round(VolumeSlider.Value), _muted, _audioPreset, _audioBoost,
                _autoHideFullscreenBar, _defaultSubtitleAppearanceDirty ? _defaultSubtitleAppearance : ReadPreferences().SubtitleStyle,
                _videoFraming, _preferHardwareDecoding));
        _ready = false;
        DisposePlayback();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnMaximize(sender, e);
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            var position = e.GetPosition(this);
            var screenPoint = PointToScreen(position);
            var dpi = VisualTreeHelper.GetDpi(this);
            var proportion = position.X / ActualWidth;
            WindowState = WindowState.Normal;
            Left = screenPoint.X / dpi.DpiScaleX - Width * proportion;
            Top = screenPoint.Y / dpi.DpiScaleY - position.Y;
        }
        DragMove();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null)
            return;

        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        var label = maximized ? "Restaurar ventana" : "Maximizar ventana";
        MaximizeButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeButton, label);

        LayoutUpdated -= OnMaximizedLayoutUpdated;
        if (maximized)
            LayoutUpdated += OnMaximizedLayoutUpdated;
        else
            Shell.Margin = new Thickness(_fullscreen ? 0 : 1);
    }

    private void OnMaximizedLayoutUpdated(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var workArea = Forms.Screen.FromHandle(handle).WorkingArea;
        var origin = PointFromScreen(new Point(workArea.Left, workArea.Top));
        var end = PointFromScreen(new Point(workArea.Right, workArea.Bottom));
        var margin = new Thickness(Math.Max(0, origin.X), Math.Max(0, origin.Y),
            Math.Max(0, ActualWidth - end.X), Math.Max(0, ActualHeight - end.Y));

        if (Shell.Margin != margin)
            Shell.Margin = margin;
    }

    private void OnTogglePanel(object sender, RoutedEventArgs e)
    {
        _panelVisible = !_panelVisible;
        if (!_panelVisible && SidePanel.IsKeyboardFocusWithin)
        {
            if (_fullscreen)
                ShowFullscreenBar();
            PanelButton.Focus();
        }
        UpdatePanelVisibility();
        if (_fullscreen && sender == PanelButton)
            ShowFullscreenBar();
    }

    private void ShowImagePanel() => ShowSidePanel(ImageTab);

    private void ShowInfoPanel(string title, FrameworkElement content)
    {
        _infoPanelTitle = title;
        InfoPanel.Content = content;
        ShowSidePanel(InfoTab);
    }

    private void ShowSidePanel(RadioButton tab)
    {
        _panelVisible = true;
        if (tab.IsChecked == true)
            UpdatePanelContent();
        else
            tab.IsChecked = true;
        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility(bool animate = true)
    {
        UpdatePanelTransition(animate);
        var action = _panelVisible ? "Cerrar panel lateral" : "Abrir panel lateral";
        PanelButton.Content = _panelVisible;
        PanelButton.ToolTip = action + " (Ctrl+L)";
        AutomationProperties.SetName(PanelButton, action);
        UpdateViewportLayout();
    }

    private void UpdateViewportLayout()
    {
        var flush = _fullscreen || _videoVisible;
        ContentArea.Margin = flush ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        Viewport.CornerRadius = new CornerRadius(flush ? 0 : 12);
        Viewport.BorderThickness = new Thickness(flush ? 0 : 1);
        Transport.Margin = new Thickness(0, flush ? 0 : 8, 0, 0);
    }

    private void OnPanelTabChanged(object sender, RoutedEventArgs e) => UpdatePanelContent();

    private void UpdatePanelContent()
    {
        if (InfoPanel is null)
            return;
        QueuePanel.Visibility = QueueTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = ImageTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SubtitlePanel.Visibility = SubtitleTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = InfoTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SubtitleTab.IsChecked == true)
            EnsureSubtitleSettings();
        SidePanelTitle.Text = SubtitleTab.IsChecked == true ? "Ajustes de subtítulos"
            : ImageTab.IsChecked == true ? "Ajustes de imagen"
            : InfoTab.IsChecked == true ? _infoPanelTitle : "Cola de reproducción";
        SidePanel.Width = Math.Min(420, Math.Max(280, ActualWidth * 0.48));
        if (ImageTab.IsChecked == true || QueueTab.IsChecked == true)
            SidePanel.Width = 258;
    }

    private void OnViewportClick(object sender, MouseButtonEventArgs e)
    {
        if (Transport.IsMouseOver)
            return;
        if (e.ClickCount == 2 && _current is not null)
        {
            e.Handled = true;
            ToggleFullscreen();
        }
        Focus();
    }

    private void OnFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_fullscreen)
        {
            _fullscreen = false;
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, _windowedChrome);
            Left = _windowedBounds.Left;
            Top = _windowedBounds.Top;
            Width = _windowedBounds.Width;
            Height = _windowedBounds.Height;
            WindowState = _windowedState;
        }
        else
        {
            _panelVisible = false;
            var handle = new WindowInteropHelper(this).Handle;
            var screen = Forms.Screen.FromHandle(handle).Bounds;
            _windowedState = WindowState;
            _windowedBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            _windowedChrome = WindowChrome.GetWindowChrome(this);
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            SetWindowPos(handle, new IntPtr(-1), screen.X, screen.Y, screen.Width, screen.Height, 0x0010);
        }

        Header.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        Shell.RowDefinitions[0].Height = new GridLength(_fullscreen ? 0 : 64);
        FullscreenIcon.Text = _fullscreen ? "\uE73F" : "\uE740";
        var label = _fullscreen ? "Salir de pantalla completa (Esc)" : "Pantalla completa (F)";
        FullscreenButton.ToolTip = label;
        AutomationProperties.SetName(FullscreenButton, label);
        UpdateTransportHost();
        ShowFullscreenBar();
        _keyboardBarPointer = null;
        _fullscreenPointer.IsEnabled = _fullscreen && _autoHideFullscreenBar;
        UpdatePanelVisibility(animate: false);
        OnWindowStateChanged(this, EventArgs.Empty);
    }

    private void SetFullscreenBarAutoHide(bool autoHide)
    {
        _autoHideFullscreenBar = autoHide;
        _fullscreenPointer.IsEnabled = _fullscreen && autoHide;
        ShowFullscreenBar();
    }

    private void ShowFullscreenBar(long visibilityMilliseconds = FullscreenBarDelay)
    {
        _fullscreenBarVisibleUntil = Environment.TickCount64 + visibilityMilliseconds;
        SetTransportVisible(true);
    }

    private void UpdateTransportHost()
    {
        var host = !_fullscreen ? Shell : _videoVisible ? VideoOverlay : IdleTransportHost;
        if (Transport.Parent == host)
            return;
        ((Panel)Transport.Parent).Children.Remove(Transport);
        host.Children.Add(Transport);
        Grid.SetRow(Transport, _fullscreen ? 0 : 2);
        Transport.VerticalAlignment = _fullscreen ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        PositionTransport(animate: false);
    }

    private void PositionTransport(bool animate)
    {
        var shown = !_fullscreen || _fullscreenBarShown;
        var height = Transport.ActualHeight;
        var target = shown ? 0 : height;
        var start = Transport.Visibility == Visibility.Hidden ? height : TransportSlide.Y;
        var captionStart = CaptionSlide.Y;
        var captionTarget = shown && Transport.Parent == VideoOverlay ? -Math.Max(0, height - 18) : 0;
        Transport.IsHitTestVisible = shown;

        TransportSlide.BeginAnimation(TranslateTransform.YProperty, null);
        CaptionSlide.BeginAnimation(TranslateTransform.YProperty, null);

        var distance = Math.Abs(target - start);
        if (!animate || !SystemParameters.ClientAreaAnimation || distance < 0.5)
        {
            TransportSlide.Y = target;
            CaptionSlide.Y = captionTarget;
            Transport.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
            return;
        }

        TransportSlide.Y = start;
        CaptionSlide.Y = captionStart;
        var milliseconds = Math.Clamp(FullscreenBarSlideMilliseconds * distance / Math.Max(1, height), 100, FullscreenBarSlideMilliseconds);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        var slide = new DoubleAnimation(start, target, duration) { EasingFunction = easing };
        slide.Completed += (_, _) =>
        {
            if (!shown && _fullscreen && !_fullscreenBarShown)
                Transport.Visibility = Visibility.Hidden;
            TransportSlide.Y = target;
            CaptionSlide.Y = captionTarget;
            TransportSlide.BeginAnimation(TranslateTransform.YProperty, null);
            CaptionSlide.BeginAnimation(TranslateTransform.YProperty, null);
        };
        Transport.Visibility = Visibility.Visible;
        TransportSlide.BeginAnimation(TranslateTransform.YProperty, slide);
        CaptionSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(captionStart, captionTarget, duration)
        {
            EasingFunction = easing
        });
    }

    private void SetTransportVisible(bool visible)
    {
        if (_fullscreenBarShown == visible)
            return;
        _fullscreenBarShown = visible;
        PositionTransport(animate: _fullscreen);
    }

    private void UpdatePanelTransition(bool animate)
    {
        var element = SidePanel;
        var slide = SidePanelSlide;
        var visible = _panelVisible;
        var wasInteractive = element.IsHitTestVisible;
        element.IsHitTestVisible = visible;
        var property = TranslateTransform.XProperty;
        const double distance = 16;
        var collapsed = element.Visibility == Visibility.Collapsed;
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(OpacityProperty, null);
            slide.BeginAnimation(property, null);
            element.Opacity = visible ? 1 : 0;
            slide.SetValue(property, visible ? 0d : distance);
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (visible && wasInteractive && !collapsed && element.Opacity == 1)
            return;
        if (!visible && collapsed)
            return;

        var duration = TimeSpan.FromMilliseconds(visible ? 180 : 220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var alpha = new DoubleAnimation(collapsed ? 0 : element.Opacity, visible ? 1 : 0, duration) { EasingFunction = easing };
        var offset = new DoubleAnimation(collapsed ? distance : (double)slide.GetValue(property), visible ? 0 : distance, duration)
        {
            EasingFunction = easing
        };
        alpha.Completed += (_, _) =>
        {
            if (!visible && !element.IsHitTestVisible)
                element.Visibility = Visibility.Collapsed;
        };
        element.Visibility = Visibility.Visible;
        element.BeginAnimation(OpacityProperty, alpha);
        slide.BeginAnimation(property, offset);
    }

    private void UpdateFullscreenBar()
    {
        if (!_fullscreen || !_autoHideFullscreenBar)
            return;

        // LibVLC shows video in a native window, so WPF mouse events alone don't see the bottom edge.
        var host = _videoVisible ? VideoOverlay : IdleTransportHost;
        if (!host.IsVisible || PresentationSource.FromVisual(host) is null)
            return;
        var cursor = Forms.Cursor.Position;
        var pointer = host.PointFromScreen(new Point(cursor.X, cursor.Y));
        var revealHeight = Math.Max(FullscreenBarRevealHeight, Transport.ActualHeight - TransportSlide.Y);
        var overBar = pointer.X >= 0 && pointer.X < host.ActualWidth
            && pointer.Y >= host.ActualHeight - revealHeight && pointer.Y < host.ActualHeight;
        if (_keyboardBarPointer != cursor)
            _keyboardBarPointer = null;
        var usingKeyboard = _keyboardBarPointer.HasValue && Transport.IsKeyboardFocusWithin;
        if (overBar || usingKeyboard || Transport.IsMouseCaptureWithin || _openMenu?.IsOpen == true)
            ShowFullscreenBar(FullscreenBarLeaveDelay);
        else if (_fullscreenBarShown && Environment.TickCount64 >= _fullscreenBarVisibleUntil)
        {
            if (Transport.IsKeyboardFocusWithin)
                Focus();
            SetTransportVisible(false);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_fullscreen && _autoHideFullscreenBar && (key == Key.Tab || Transport.IsKeyboardFocusWithin))
        {
            _keyboardBarPointer = Forms.Cursor.Position;
            ShowFullscreenBar();
        }
        var modifiers = Keyboard.Modifiers;
        if (Keyboard.FocusedElement is TextBoxBase && key is Key.Left or Key.Right or Key.Space)
            return;
        if ((modifiers == ModifierKeys.Shift && key == Key.F10)
            || (modifiers == ModifierKeys.None && key == Key.Apps))
            ShowViewportMenu(PlacementMode.Center);
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.O)
            OpenFiles(play: false);
        else if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.O: OpenFiles(play: true); break;
                case Key.T: OpenSubtitle(); break;
                case Key.Left when CanAdjustSubtitleDelay: ChangeSubtitleDelay(-SubtitleDelayStep); break;
                case Key.Right when CanAdjustSubtitleDelay: ChangeSubtitleDelay(SubtitleDelayStep); break;
                case Key.D0 or Key.NumPad0 when CanAdjustSubtitleDelay: SetSubtitleDelay(0); break;
                case Key.L: OnTogglePanel(this, e); break;
                case Key.E: ShowImagePanel(); break;
                case Key.S: SaveSnapshot(); break;
                default: return;
            }
        }
        else if (modifiers == ModifierKeys.Alt && key == Key.Enter)
            ToggleFullscreen();
        else if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Space when e.OriginalSource is not Button and not RadioButton
                    and not ListBoxItem: PlayPause(); break;
                case Key.Left when e.OriginalSource is not Slider and not ListBoxItem: Skip(-5_000); break;
                case Key.Right when e.OriginalSource is not Slider and not ListBoxItem: Skip(5_000); break;
                case Key.Escape when _panelVisible: OnTogglePanel(this, e); break;
                case Key.Escape when _fullscreen:
                case Key.F: ToggleFullscreen(); break;
                case Key.M: ToggleMute(); break;
                default: return;
            }
        }
        else
            return;
        e.Handled = true;
    }

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumenPlayer", "preferences.json");

    private static PlayerPreferences ReadPreferences() =>
        JsonSettingsFile.Read<PlayerPreferences>(SettingsFile) ?? new(70, false);

    private static bool SavePreferences(PlayerPreferences preferences) => JsonSettingsFile.Save(SettingsFile, preferences);

    private sealed record PlayerPreferences(int Volume, bool Muted, int AudioPreset = 0, int AudioBoost = 0,
        bool AutoHideFullscreenBar = true, SubtitleAppearance? SubtitleStyle = null, VideoFraming Framing = VideoFraming.FitWidth,
        bool HardwareDecoding = false);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
