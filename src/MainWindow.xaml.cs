using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _ready;
    private bool _fullscreen;
    private bool _panelVisible = true;
    private bool _muted;
    private WindowState _windowedState;
    private Rect _windowedBounds;
    private WindowChrome? _windowedChrome;
    private System.Drawing.Point _lastPointer;
    private long _lastFullscreenInput;
    private bool _transportHidden;
    private ContextMenu? _openMenu;

    public MainWindow()
    {
        InitializeComponent();
        QueueList.ItemsSource = _queue;
        _effectTimer.Tick += OnEffectTick;
        _clock.Tick += (_, _) =>
        {
            RefreshPlayback();
            RefreshFullscreenControls();
        };
        Activated += (_, _) => { if (_fullscreen) Topmost = true; };
        Deactivated += (_, _) => { if (_fullscreen) Topmost = false; };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var preferences = ReadPreferences();
        VolumeSlider.Value = Math.Clamp(preferences.Volume, 0, 100);
        _muted = preferences.Muted;
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
        _clock.Stop();
        _effectTimer.Stop();
        _subtitleGeneration?.Cancel();
        if (_ready)
            SavePreferences(new((int)Math.Round(VolumeSlider.Value), _muted, _audioPreset, _audioBoost));
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

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateWindowMargins();

    private void UpdateWindowMargins()
    {
        if (Shell is null)
            return;

        Shell.Margin = _fullscreen ? new Thickness(0) : WindowState == WindowState.Maximized
            ? SystemParameters.WindowResizeBorderThickness
            : new Thickness(1);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnTogglePanel(object sender, RoutedEventArgs e)
    {
        _panelVisible = !_panelVisible;
        UpdatePanelVisibility();
    }

    private void ShowImagePanel()
    {
        if (_fullscreen)
            ToggleFullscreen();
        _panelVisible = true;
        ImageTab.IsChecked = true;
        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        SidePanel.Visibility = _panelVisible && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        PanelButton.Foreground = (Brush)FindResource(_panelVisible ? "TextBrush" : "MutedBrush");
    }

    private void OnPanelTabChanged(object sender, RoutedEventArgs e)
    {
        if (QueuePanel is null || ImagePanel is null)
            return;
        QueuePanel.Visibility = QueueTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = ImageTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewportClick(object sender, MouseButtonEventArgs e)
    {
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
            WindowChrome.SetWindowChrome(this, _windowedChrome);
            Left = _windowedBounds.Left;
            Top = _windowedBounds.Top;
            Width = _windowedBounds.Width;
            Height = _windowedBounds.Height;
            WindowState = _windowedState;
        }
        else
        {
            var handle = new WindowInteropHelper(this).Handle;
            var screen = Forms.Screen.FromHandle(handle).Bounds;
            _windowedState = WindowState;
            _windowedBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            _windowedChrome = WindowChrome.GetWindowChrome(this);
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null);
            Topmost = true;
            SetWindowPos(handle, new IntPtr(-1), screen.X, screen.Y, screen.Width, screen.Height, 0x0010);
        }

        RevealFullscreenControls();
        Header.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        Shell.RowDefinitions[0].Height = new GridLength(_fullscreen ? 0 : 64);
        ContentArea.Margin = _fullscreen ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        Viewport.CornerRadius = new CornerRadius(_fullscreen ? 0 : 12);
        Viewport.BorderThickness = new Thickness(_fullscreen ? 0 : 1);
        FullscreenIcon.Text = _fullscreen ? "\uE73F" : "\uE740";
        var label = _fullscreen ? "Salir de pantalla completa (Esc)" : "Pantalla completa (F)";
        FullscreenButton.ToolTip = label;
        AutomationProperties.SetName(FullscreenButton, label);
        UpdatePanelVisibility();
        UpdateWindowMargins();
    }

    private void RefreshFullscreenControls()
    {
        if (!_fullscreen)
            return;

        var pointer = Forms.Control.MousePosition;
        var point = PointFromScreen(new Point(pointer.X, pointer.Y));
        var overControls = point.X >= 0 && point.X < ActualWidth && point.Y >= ActualHeight - 72 && point.Y < ActualHeight;
        if (pointer != _lastPointer || overControls || _openMenu?.IsOpen == true || _player?.IsPlaying != true)
        {
            RevealFullscreenControls();
            return;
        }

        if (Environment.TickCount64 - _lastFullscreenInput >= 2_500)
            SetTransportHidden(true);
    }

    private void RevealFullscreenControls()
    {
        _lastPointer = Forms.Control.MousePosition;
        _lastFullscreenInput = Environment.TickCount64;
        SetTransportHidden(false);
    }

    private void SetTransportHidden(bool hidden)
    {
        if (_transportHidden == hidden)
            return;

        _transportHidden = hidden;
        Transport.IsHitTestVisible = !hidden;
        var from = Transport.Opacity;
        var to = hidden ? 0d : 1d;
        Transport.BeginAnimation(OpacityProperty, null);
        Transport.Opacity = to;
        if (!hidden)
            Transport.Visibility = Visibility.Visible;
        if (!SystemParameters.ClientAreaAnimation)
        {
            Transport.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        var fade = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(160))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (hidden)
            fade.Completed += (_, _) =>
            {
                if (_transportHidden)
                    Transport.Visibility = Visibility.Collapsed;
            };
        Transport.BeginAnimation(OpacityProperty, fade);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_fullscreen)
            RevealFullscreenControls();
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
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

    private static PlayerPreferences ReadPreferences()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(SettingsFile)) ?? new(70, false)
                : new(70, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(70, false);
        }
    }

    private static void SavePreferences(PlayerPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(preferences));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Lumen Player: {0}", ex.Message);
        }
    }

    private sealed record PlayerPreferences(int Volume, bool Muted, int AudioPreset = 0, int AudioBoost = 0);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
