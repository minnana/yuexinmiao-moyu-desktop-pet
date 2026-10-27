using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YueXinCat.Models;
using YueXinCat.Services;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImage = System.Drawing.Image;
using DrawingImageAnimator = System.Drawing.ImageAnimator;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace YueXinCat;

public partial class MainWindow : Window
{
    private readonly StateStore _store = new();
    private readonly KeyboardHook _keyboardHook = new();
    private readonly TrayIconService _tray = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _keyPulseTimer;
    private readonly TranslateTransform _normalTransform = new();
    private readonly TranslateTransform _keyboardTransform = new();
    private readonly TranslateTransform _incomeTransform = new();
    private readonly ScaleTransform _keyTransform = new(1, 1);
    private readonly ScaleTransform _keyboardOverlayTransform = new(1, 1);
    private AppState _appState = new();
    private SalaryEngine _engine = new(new SalarySettings());
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset _incomeVisibleUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _keyPulseUntil = DateTimeOffset.MinValue;
    private bool _dragging;
    private bool _paused;
    private DrawingImage? _normalGifImage;
    private Stream? _normalGifStream;

    private const double NormalCatBaseWidth = 214;
    private const double NormalCatBaseHeight = 230;
    private const double KeyboardBaseWidth = 285;
    private const double KeyboardBaseHeight = 237;

    private static readonly IReadOnlyDictionary<string, Point> KeyboardKeyPositions =
        new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase)
        {
            ["Q"] = new(62, 170), ["W"] = new(88, 170), ["E"] = new(114, 170),
            ["R"] = new(140, 170), ["T"] = new(166, 170), ["Y"] = new(192, 170),
            ["A"] = new(75, 185), ["S"] = new(101, 185), ["D"] = new(127, 185),
            ["F"] = new(153, 185), ["G"] = new(179, 185), ["H"] = new(205, 185),
            ["Z"] = new(88, 199), ["X"] = new(114, 199), ["C"] = new(140, 199),
            ["V"] = new(166, 199), ["B"] = new(192, 199), ["Space"] = new(132, 199)
        };

    public MainWindow()
    {
        InitializeComponent();
        NormalCatImage.RenderTransform = _normalTransform;
        KeyboardRaisedImage.RenderTransform = _keyboardTransform;
        KeyboardDownImage.RenderTransform = _keyboardTransform;
        FloatingIncome.RenderTransform = _incomeTransform;
        FloatingIncome.RenderTransformOrigin = new Point(0.5, 1);
        KeyboardKeyOverlay.RenderTransform = _keyboardOverlayTransform;
        KeyboardKeyOverlay.RenderTransformOrigin = new Point(0, 0);
        KeyCap.RenderTransform = _keyTransform;
        _keyboardHook.KeyChanged += KeyboardHook_KeyChanged;
        _tray.OpenRequested += Tray_OpenRequested;
        _tray.KeyboardToggleRequested += Tray_KeyboardToggleRequested;
        _tray.PauseToggleRequested += Tray_PauseToggleRequested;
        _tray.ExitRequested += Tray_ExitRequested;
        // Salary and balance are deliberately redrawn once per wall-clock
        // second. The GIF animation runs independently from this salary timer.
        // Keeping this work off the render priority prevents money calculation
        // from making the normal-mode GIF stutter.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _keyPulseTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _keyPulseTimer.Tick += (_, _) => ResetKeyboardPulseIfNeeded(DateTimeOffset.Now);
        LoadNormalGifAnimation();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _appState = _store.Load();
            _engine = new SalaryEngine(_appState.Settings);
            _paused = _appState.Salary.IsPaused;
        }
        catch
        {
            _appState = new AppState();
            _engine = new SalaryEngine(_appState.Settings);
        }

        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            Left = Math.Max(12, SystemParameters.WorkArea.Right - Width - 24);
            Top = Math.Max(12, SystemParameters.WorkArea.Bottom - Height - 30);
        }

        var now = DateTimeOffset.Now;
        // Catch up the saved balance before starting the live clock.
        if (!_paused && _appState.Salary.BalanceAsOf <= now)
            _appState.Salary.Balance += _engine.CalculateIncrement(_appState.Salary.BalanceAsOf, now);
        _appState.Salary.BalanceAsOf = now;
        _appState.Salary.IsPaused = _paused;
        _lastSave = now;
        ApplyPetScale(_appState.Settings.PetSizePercent);
        ApplyKeyboardMode(_appState.Settings.KeyboardTrackingEnabled);
        _tray.Initialize(_appState.Settings.KeyboardTrackingEnabled, _paused);
        UpdateDisplay(now, 0m);
        _timer.Start();
        _keyPulseTimer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        _keyPulseTimer.Stop();
        if (_normalGifImage is not null)
        {
            DrawingImageAnimator.StopAnimate(_normalGifImage, NormalGifFrameChanged);
            _normalGifImage.Dispose();
            _normalGifImage = null;
        }
        _normalGifStream?.Dispose();
        _normalGifStream = null;
        _keyboardHook.Dispose();
        _tray.Dispose();
        var now = DateTimeOffset.Now;
        if (!_paused && _appState.Salary.BalanceAsOf <= now)
            _appState.Salary.Balance += _engine.CalculateIncrement(_appState.Salary.BalanceAsOf, now);
        _appState.Salary.BalanceAsOf = now;
        _appState.Salary.IsPaused = _paused;
        try { _store.Save(_appState); } catch { /* keep closing even if the profile is locked */ }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        if (_paused)
        {
            _appState.Salary.BalanceAsOf = now;
            _appState.Salary.IsPaused = true;
            UpdateDisplay(now, 0m);
            return;
        }
        var increment = 0m;
        if (_appState.Salary.BalanceAsOf <= now)
        {
            increment = _engine.CalculateIncrement(_appState.Salary.BalanceAsOf, now);
            _appState.Salary.Balance += increment;
        }
        _appState.Salary.BalanceAsOf = now;
        // The timer itself is one second. The visible amount is always the
        // configured second wage, while the balance above keeps exact elapsed
        // fractional seconds for catch-up and persistence.
        var visibleSecondPay = increment > 0m
            ? _engine.GetSecondPay(DateOnly.FromDateTime(now.DateTime))
            : 0m;
        UpdateDisplay(now, visibleSecondPay);

        if ((now - _lastSave).TotalSeconds >= 5)
        {
            try { _store.Save(_appState); _lastSave = now; } catch { /* retry on the next tick */ }
        }
    }

    private void UpdateDisplay(DateTimeOffset now, decimal increment)
    {
        // The normal mode moves through the supplied GIF frames. Keyboard mode
        // keeps its body still so only the two paw frames visibly move.
        _normalTransform.Y = 0;
        _keyboardTransform.Y = 0;
        ResetKeyboardPulseIfNeeded(now);

        var snapshot = _engine.GetSnapshot(_appState.Salary, now, LastKeyText.Text == "待机" ? null : LastKeyText.Text);
        BalanceText.Text = FormatMoney(snapshot.Balance);
        BalanceText.Foreground = snapshot.Balance < 0 ? new SolidColorBrush(Color.FromRgb(217, 109, 127)) : new SolidColorBrush(Color.FromRgb(50, 116, 95));
        RateText.Text = snapshot.SecondPay <= 0
            ? "当前没有可计薪时段"
            : $"摸鱼每秒  +¥{snapshot.SecondPay:0.000000}";
        if (_paused)
        {
            RateText.Text = "计薪已暂停";
            StatusText.Text = "已暂停计薪";
        }
        else
        {
            StatusText.Text = snapshot.Status switch
            {
                "加班中" => "加班中，也在赚",
                "工作中" => "工作中，也在赚",
                "休息日" => "休息日，暂停计薪",
                _ => "下班啦，暂停计薪"
            };
        }

        if (increment > 0m)
        {
            FloatingIncomeText.Text = $"+¥{increment:0.000000}";
            FloatingIncome.Visibility = Visibility.Visible;
            FloatingIncome.Opacity = 0.98;
            _incomeVisibleUntil = now.AddSeconds(1);
            AnimateIncomeBubble();
        }
        else if (now >= _incomeVisibleUntil)
        {
            FloatingIncome.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatMoney(decimal value)
    {
        var sign = value < 0 ? "−" : "";
        return $"{sign}¥{Math.Abs(value):N2}";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_appState.Settings, _appState.Salary.Balance) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSettings is null)
            return;

        _appState.Settings = dialog.SavedSettings;
        _appState.Salary.Balance = dialog.SavedBalance;
        _appState.Salary.BalanceAsOf = DateTimeOffset.Now;
        _engine = new SalaryEngine(_appState.Settings);
        ApplyPetScale(_appState.Settings.PetSizePercent);
        ApplyKeyboardMode(_appState.Settings.KeyboardTrackingEnabled);
        var startup = new StartupService();
        _ = startup.SetEnabled(_appState.Settings.StartWithWindows);
        try { _store.Save(_appState); } catch { }
        UpdateDisplay(DateTimeOffset.Now, 0m);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void KeyboardToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = !_appState.Settings.KeyboardTrackingEnabled;
        _appState.Settings.KeyboardTrackingEnabled = enabled;
        ApplyKeyboardMode(enabled);
        try { _store.Save(_appState); } catch { }
    }

    private void ApplyKeyboardMode(bool enabled)
    {
        _appState.Settings.KeyboardTrackingEnabled = enabled;
        _keyPulseUntil = DateTimeOffset.MinValue;
        _keyTransform.ScaleX = 1;
        _keyTransform.ScaleY = 1;
        NormalCatImage.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        KeyboardRaisedImage.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        KeyboardDownImage.Visibility = Visibility.Collapsed;
        KeyboardPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        KeyboardKeyOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        KeyboardToggleButton.Content = enabled ? "↩  普通模式" : "⌨  键盘跟随";
        if (enabled)
        {
            _keyboardHook.Start();
            DragHint.Text = "拖动月薪喵移动 · 按键会跟着敲";
        }
        else
        {
            _keyboardHook.Stop();
            DragHint.Text = "拖动月薪喵移动 · 点击齿轮设置";
            LastKeyText.Text = "待机";
            KeyboardHighlight.Visibility = Visibility.Collapsed;
            ActiveKeyBadge.Visibility = Visibility.Collapsed;
        }
        _tray.UpdateState(enabled, _paused);
    }

    private void ApplyPetScale(int percent)
    {
        var scale = Math.Clamp(percent, 40, 150) / 100d;
        NormalCatImage.Width = NormalCatBaseWidth * scale;
        NormalCatImage.Height = NormalCatBaseHeight * scale;
        KeyboardRaisedImage.Width = KeyboardBaseWidth * scale;
        KeyboardRaisedImage.Height = KeyboardBaseHeight * scale;
        KeyboardDownImage.Width = KeyboardBaseWidth * scale;
        KeyboardDownImage.Height = KeyboardBaseHeight * scale;
        _keyboardOverlayTransform.ScaleX = scale;
        _keyboardOverlayTransform.ScaleY = scale;
    }

    private void Tray_OpenRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void Tray_KeyboardToggleRequested(object? sender, EventArgs e)
    {
        var enabled = !_appState.Settings.KeyboardTrackingEnabled;
        _appState.Settings.KeyboardTrackingEnabled = enabled;
        ApplyKeyboardMode(enabled);
        try { _store.Save(_appState); } catch { }
    }

    private void Tray_PauseToggleRequested(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        if (!_paused && _appState.Salary.BalanceAsOf <= now)
            _appState.Salary.Balance += _engine.CalculateIncrement(_appState.Salary.BalanceAsOf, now);
        _appState.Salary.BalanceAsOf = now;
        _paused = !_paused;
        _appState.Salary.IsPaused = _paused;
        _tray.UpdateState(_appState.Settings.KeyboardTrackingEnabled, _paused);
        try { _store.Save(_appState); } catch { }
        UpdateDisplay(now, 0m);
    }

    private void Tray_ExitRequested(object? sender, EventArgs e) => Close();

    private void KeyboardHook_KeyChanged(object? sender, YueXinCat.Services.KeyboardEventArgs e)
    {
        if (e.IsInjected)
            return;
        if (!Dispatcher.CheckAccess())
        {
            // Low-level hook callbacks can arrive from the hook thread. Input
            // priority keeps a rapid key-up from waiting behind normal UI work.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => KeyboardHook_KeyChanged(sender, e)));
            return;
        }
        if (!_appState.Settings.KeyboardTrackingEnabled)
            return;
        if (!e.IsDown)
        {
            // Release is the source of truth for the raised-paw state. The
            // timer remains only as a fallback if Windows drops an event.
            ReleaseKeyboardPulse();
            return;
        }
        LastKeyText.Text = e.Label.Length > 9 ? e.Label[..9] : e.Label;
        ActiveKeyText.Text = LastKeyText.Text;
        var keyLabel = NormalizeKeyLabel(e.Label);
        if (KeyboardKeyPositions.TryGetValue(keyLabel, out var keyPosition))
        {
            System.Windows.Controls.Canvas.SetLeft(KeyboardHighlight, keyPosition.X);
            System.Windows.Controls.Canvas.SetTop(KeyboardHighlight, keyPosition.Y);
        }
        else
        {
            System.Windows.Controls.Canvas.SetLeft(KeyboardHighlight, 125);
            System.Windows.Controls.Canvas.SetTop(KeyboardHighlight, 185);
        }
        ActiveKeyBadge.Visibility = Visibility.Visible;
        KeyboardHighlight.Visibility = Visibility.Visible;
        KeyboardRaisedImage.Visibility = Visibility.Collapsed;
        KeyboardDownImage.Visibility = Visibility.Visible;
        _keyPulseUntil = DateTimeOffset.Now.AddMilliseconds(240);
        _keyTransform.ScaleX = 0.92;
        _keyTransform.ScaleY = 0.92;
        KeyCap.Background = new SolidColorBrush(Color.FromRgb(223, 247, 237));
    }

    private void ReleaseKeyboardPulse()
    {
        _keyPulseUntil = DateTimeOffset.MinValue;
        _keyTransform.ScaleX = 1;
        _keyTransform.ScaleY = 1;
        if (!_appState.Settings.KeyboardTrackingEnabled)
            return;
        KeyboardRaisedImage.Visibility = Visibility.Visible;
        KeyboardDownImage.Visibility = Visibility.Collapsed;
        KeyboardHighlight.Visibility = Visibility.Collapsed;
        ActiveKeyBadge.Visibility = Visibility.Collapsed;
        KeyCap.Background = new SolidColorBrush(Color.FromRgb(242, 237, 242));
    }

    private static string NormalizeKeyLabel(string label) => label switch
    {
        "Space" or "空格" => "Space",
        _ => label.Length == 1 ? label.ToUpperInvariant() : label
    };

    private void LoadNormalGifAnimation()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/YueXinCat;component/assets/monthly_cat_normal.gif", UriKind.Absolute));
            if (resource is null)
                return;
            _normalGifStream = resource.Stream;
            _normalGifImage = DrawingImage.FromStream(_normalGifStream, useEmbeddedColorManagement: false, validateImageData: true);
            RefreshNormalGifFrame();
            DrawingImageAnimator.Animate(_normalGifImage, NormalGifFrameChanged);
        }
        catch
        {
            _normalGifImage?.Dispose();
            _normalGifImage = null;
            _normalGifStream?.Dispose();
            _normalGifStream = null;
            // The PNG remains as the XAML fallback if the GIF cannot be opened.
        }
    }

    private void NormalGifFrameChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RefreshNormalGifFrame));
        }
        catch (InvalidOperationException)
        {
            // The native GIF timer can race with window shutdown.
        }
    }

    private void RefreshNormalGifFrame()
    {
        if (_normalGifImage is null || _appState.Settings.KeyboardTrackingEnabled)
            return;

        DrawingImageAnimator.UpdateFrames(_normalGifImage);
        var source = CreateBitmapSource(_normalGifImage);
        if (source is not null)
            NormalCatImage.Source = source;
    }

    private static BitmapSource? CreateBitmapSource(DrawingImage image)
    {
        try
        {
            using var bitmap = new DrawingBitmap(image);
            using var output = new MemoryStream();
            bitmap.Save(output, DrawingImageFormat.Png);
            output.Position = 0;
            var source = BitmapFrame.Create(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private void ResetKeyboardPulseIfNeeded(DateTimeOffset now)
    {
        if (now < _keyPulseUntil || KeyboardDownImage.Visibility != Visibility.Visible)
            return;

        _keyTransform.ScaleX = 1;
        _keyTransform.ScaleY = 1;
        if (_appState.Settings.KeyboardTrackingEnabled)
        {
            KeyboardRaisedImage.Visibility = Visibility.Visible;
            KeyboardDownImage.Visibility = Visibility.Collapsed;
            KeyboardHighlight.Visibility = Visibility.Collapsed;
            ActiveKeyBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void AnimateIncomeBubble()
    {
        _incomeTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(420),
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        });
        _incomeTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        _dragging = true;
        try { DragMove(); } catch (InvalidOperationException) { }
        _dragging = false;
        e.Handled = true;
    }

    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging)
            e.Handled = true;
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        e.Handled = true;
    }
}
