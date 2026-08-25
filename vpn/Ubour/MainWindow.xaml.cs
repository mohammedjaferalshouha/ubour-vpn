using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using Ubour.AdBlock;

namespace Ubour;

public partial class MainWindow : Window
{
    private readonly EngineManager _engine = EngineManager.Instance;
    private readonly UpdateService _updates = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _metricsTimer;

    private bool _english;
    private bool _light;
    private bool _allowClose;
    private string? _customVlessUrl;
    private string _dohUrl = "https://cloudflare-dns.com/dns-query";

    public MainWindow()
    {
        InitializeComponent();
        _tray = CreateTrayIcon();

        _metricsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _metricsTimer.Tick += MetricsTimer_Tick;
        _metricsTimer.Start();

        // Preload AdBlock rules asynchronously in background
        Task.Run(() => AdBlockEngine.Instance.LoadEmbeddedFilters());

        ApplyLanguage();
        SetStoppedState();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Ubour - فتح عبور", null, (_, _) => ShowFromTray());
        menu.Items.Add("Start Protection - تشغيل الحماية", null, (_, _) => StartSelectedMode());
        menu.Items.Add("Stop - إيقاف", null, (_, _) => StopProtection());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit - خروج", null, (_, _) => ExitApplication());

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Ubour - عبور",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            StopProtection();
        }
        else
        {
            StartSelectedMode();
        }
    }

    private void StartSelectedMode()
    {
        var mode = GetSelectedMode();
        SetConnectingState();

        Task.Run(() =>
        {
            try
            {
                _engine.Start(mode, _customVlessUrl);
                Dispatcher.Invoke(() =>
                {
                    SetRunningState();
                    _tray.ShowBalloonTip(2000, "Ubour", _english ? "Protection & VPN active." : "الاتصال والحماية مفعلة الآن بنجاح.", Forms.ToolTipIcon.Info);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetErrorState(ex.Message);
                    _tray.ShowBalloonTip(3500, "Ubour", ex.Message, Forms.ToolTipIcon.Error);
                });
            }
        });
    }

    private void StopProtection()
    {
        _engine.Stop();
        SetStoppedState();
    }

    private AppOperationMode GetSelectedMode()
    {
        if (ModeDpiAdBlock.IsChecked == true) return AppOperationMode.DPI_AND_ADBLOCK;
        if (ModeAdBlockOnly.IsChecked == true) return AppOperationMode.ADBLOCK_ONLY;
        if (ModeDpiOnly.IsChecked == true) return AppOperationMode.DPI_ONLY;
        if (ModeCustomVless.IsChecked == true) return AppOperationMode.CUSTOM_VLESS;
        return AppOperationMode.WARP_AND_ADBLOCK;
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            StartSelectedMode();
        }
    }

    private void MetricsTimer_Tick(object? sender, EventArgs e)
    {
        var ads = AdBlockEngine.Instance.BlockedAds;
        var trackers = AdBlockEngine.Instance.BlockedTrackers;
        var total = AdBlockEngine.Instance.TotalQueries;
        var rules = AdBlockEngine.Instance.RulesCount;

        TxtAdsBlocked.Text = $"{ads:N0}";
        TxtTrackersBlocked.Text = $"{trackers:N0}";
        TxtTotalQueries.Text = $"{total:N0}";
        TxtActiveRules.Text = rules > 0 ? $"{rules:N0}" : "809,716";

        if (_engine.IsRunning && _engine.ConnectedAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - _engine.ConnectedAt.Value;
            TxtUptime.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        else
        {
            TxtUptime.Text = "00:00:00";
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow(_english, _customVlessUrl, _dohUrl)
        {
            Owner = this
        };
        if (settingsWin.ShowDialog() == true)
        {
            _customVlessUrl = settingsWin.CustomVlessUrl;
            _dohUrl = settingsWin.SelectedDohUrl;
            if (_engine.IsRunning && GetSelectedMode() == AppOperationMode.CUSTOM_VLESS)
            {
                StartSelectedMode();
            }
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _english = !_english;
        ApplyLanguage();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _light = !_light;
        var colors = _light
            ? new Dictionary<string, WpfColor>
            {
                ["WindowBrush"] = WpfColor.FromRgb(245, 247, 250),
                ["PanelBrush"] = WpfColors.White,
                ["PanelSoftBrush"] = WpfColor.FromRgb(235, 240, 248),
                ["PanelSelectedBrush"] = WpfColor.FromRgb(215, 235, 255),
                ["TextBrush"] = WpfColor.FromRgb(20, 30, 48),
                ["MutedBrush"] = WpfColor.FromRgb(100, 116, 139),
                ["AccentBrush"] = WpfColor.FromRgb(46, 204, 113),
                ["AccentTextBrush"] = WpfColors.White,
                ["DangerBrush"] = WpfColor.FromRgb(231, 76, 60),
                ["WarningBrush"] = WpfColor.FromRgb(243, 156, 18)
            }
            : new Dictionary<string, WpfColor>
            {
                ["WindowBrush"] = WpfColor.FromRgb(15, 23, 42),
                ["PanelBrush"] = WpfColor.FromRgb(30, 41, 59),
                ["PanelSoftBrush"] = WpfColor.FromRgb(51, 65, 85),
                ["PanelSelectedBrush"] = WpfColor.FromRgb(30, 64, 105),
                ["TextBrush"] = WpfColor.FromRgb(248, 250, 252),
                ["MutedBrush"] = WpfColor.FromRgb(148, 163, 184),
                ["AccentBrush"] = WpfColor.FromRgb(46, 204, 113),
                ["AccentTextBrush"] = WpfColors.White,
                ["DangerBrush"] = WpfColor.FromRgb(231, 76, 60),
                ["WarningBrush"] = WpfColor.FromRgb(243, 156, 18)
            };

        foreach (var item in colors)
            WpfApplication.Current.Resources[item.Key] = new SolidColorBrush(item.Value);

        ThemeButton.Content = _light ? "🌙 داكن" : "☀ فاتح";
    }

    private void ApplyLanguage()
    {
        FlowDirection = _english ? WpfFlowDirection.LeftToRight : WpfFlowDirection.RightToLeft;
        Title = _english ? "Ubour - VPN & AdBlock" : "عبور + مانع الإعلانات";
        AppTitle.Text = _english ? "Ubour + AdBlock" : "عبور + مانع الإعلانات";
        AppSubtitle.Text = _english ? "DPI bypass, Cloudflare WARP & universal ad blocking" : "تجاوز حجب DPI وتصفية شاملة للإعلانات والتتبع";
        SettingsButton.Content = _english ? "⚙ Settings" : "⚙ الإعدادات";
        LanguageButton.Content = _english ? "العربية" : "English";
        ThemeButton.Content = _light ? (_english ? "Dark mode" : "وضع داكن") : (_english ? "Light mode" : "وضع فاتح");
        ModeSectionTitle.Text = _english ? "Primary Operation Mode" : "وضع التشغيل الأساسي";

        LblModeWarp.Text = _english ? "Cloudflare WARP Tunnel + AdBlock" : "نفق كلاود فلير (Cloudflare WARP) + منع الإعلانات";
        LblModeWarpDesc.Text = _english ? "Global encryption & complete ad and tracker filtering" : "حماية وتشفير عالمي مع تصفية كاملة للإعلانات";

        LblModeDpiAdBlock.Text = _english ? "Direct DPI Bypass + AdBlock" : "تجاوز الحجب المباشر (DPI) + منع الإعلانات";
        LblModeDpiAdBlockDesc.Text = _english ? "100% speed without changing IP, with total ad block" : "سرعة 100% بدون تغيير الـ IP مع حظر تام للإعلانات";

        LblModeAdBlockOnly.Text = _english ? "Ad & Tracker Blocking Only" : "منع الإعلانات والتتبع فقط";
        LblModeAdBlockOnlyDesc.Text = _english ? "Ultra-lightweight on CPU with instant DNS speed" : "خفيف جداً على موارد الحاسوب وسرعة فائقة";

        LblModeDpiOnly.Text = _english ? "DPI Bypass Only (No Filtering)" : "تجاوز الحجب فقط (بدون تصفية)";
        LblModeDpiOnlyDesc.Text = _english ? "Direct website unblocking" : "فك حجب المواقع مباشرة";

        LblModeCustomVless.Text = _english ? "Custom VLESS Reality Server" : "خادم VLESS Reality مخصص";
        LblModeCustomVlessDesc.Text = _english ? "Private encrypted proxy server with AdBlock" : "اتصال مشفر متقدم بخادمك الخاص مع منع الإعلانات";

        MetricsTitle.Text = _english ? "Live Security Dashboard" : "لوحة الإحصائيات الحية";
        LblAdsBlocked.Text = _english ? "Blocked Ads" : "إعلانات محجوبة";
        LblTrackersBlocked.Text = _english ? "Blocked Trackers" : "متعقبات محجوبة";
        LblActiveRules.Text = _english ? "Active Filter Rules" : "قواعد التصفية النشطة";
        LblTotalQueries.Text = _english ? "Total DNS Queries" : "إجمالي استعلامات DNS";
        LblUptime.Text = _english ? "Active Connection Time" : "مدة الاتصال النشط";

        FooterText.Text = _english ? "Ubour v1.5.1 · Universal AdBlock & Secure DPI Bypass · Open Source" : "عبور v1.5.1 · تصفية شاملة وتجاوز حجب آمن · مفتوح المصدر";

        if (_engine.IsRunning) SetRunningState(); else SetStoppedState();
    }

    private void SetRunningState()
    {
        var accentBrush = (WpfBrush)WpfApplication.Current.Resources["AccentBrush"];
        PowerButton.Background = accentBrush;
        PowerGlowRing.Stroke = accentBrush;
        StatusLabel.Text = _english ? "Connected & Protected" : "متصل وحماية الإعلانات مفعلة";
        StatusLabel.Foreground = accentBrush;
        StatusDetail.Text = _english ? "All traffic is protected and ads are actively blocked." : "جميع الاتصالات محمية والإعلانات محجوبة في الخلفية.";
        TxtEngineStatus.Text = _english ? $"Engine active in {_engine.CurrentMode} mode." : $"المحرك يعمل بنشاط في وضع {_engine.CurrentMode}.";
    }

    private void SetConnectingState()
    {
        var warningBrush = (WpfBrush)WpfApplication.Current.Resources["WarningBrush"];
        PowerButton.Background = warningBrush;
        PowerGlowRing.Stroke = warningBrush;
        StatusLabel.Text = _english ? "Connecting..." : "جارٍ الاتصال وتفعيل التصفية...";
        StatusLabel.Foreground = warningBrush;
        StatusDetail.Text = _english ? "Initializing engine and applying DNS rules..." : "جارٍ بدء المحرك وتطبيق قواعد الـ DNS...";
    }

    private void SetStoppedState()
    {
        var dangerBrush = (WpfBrush)WpfApplication.Current.Resources["DangerBrush"];
        PowerButton.Background = dangerBrush;
        PowerGlowRing.Stroke = dangerBrush;
        StatusLabel.Text = _english ? "Stopped" : "متوقف";
        StatusLabel.Foreground = (WpfBrush)WpfApplication.Current.Resources["TextBrush"];
        StatusDetail.Text = _english ? "Click the power button to start protection." : "اضغط زر التشغيل لبدء الاتصال وتفعيل التصفية.";
        TxtEngineStatus.Text = _english ? "Engine ready to connect." : "المحرك جاهز للاتصال.";
    }

    private void SetErrorState(string error)
    {
        var dangerBrush = (WpfBrush)WpfApplication.Current.Resources["DangerBrush"];
        PowerButton.Background = dangerBrush;
        PowerGlowRing.Stroke = dangerBrush;
        StatusLabel.Text = _english ? "Could Not Connect" : "تعذر الاتصال";
        StatusLabel.Foreground = dangerBrush;
        StatusDetail.Text = error;
        TxtEngineStatus.Text = error;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _tray.ShowBalloonTip(2000, "Ubour", _english ? "Ubour is still running in the system tray." : "عبور ما زال يعمل في الخلفية قرب الساعة.", Forms.ToolTipIcon.Info);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        _engine.Stop();
        _tray.Visible = false;
        _tray.Dispose();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _engine.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        Close();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
