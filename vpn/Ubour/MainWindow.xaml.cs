using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace Ubour;

public partial class MainWindow : Window
{
    private readonly EngineManager _engine = new();
    private readonly UpdateService _updates = new();
    private readonly Forms.NotifyIcon _tray;
    private bool _english;
    private bool _light;
    private bool _allowClose;
    private string? _engineUpdateUrl;

    public MainWindow()
    {
        InitializeComponent();
        _tray = CreateTrayIcon();
        ApplyLanguage();
        Loaded += async (_, _) => await CheckUpdatesAsync(silent: true);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Ubour", null, (_, _) => ShowFromTray());
        menu.Items.Add("Start", null, async (_, _) => await StartEngineAsync());
        menu.Items.Add("Stop", null, (_, _) => StopEngine());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Ubour",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) StopEngine(); else await StartEngineAsync();
    }

    private async Task StartEngineAsync()
    {
        try
        {
            _engine.Start();
            SetRunningState();
            _tray.ShowBalloonTip(1800, "Ubour", _english ? "Connection engine is running." : "محرك الاتصال يعمل الآن.", Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            SetErrorState(ex.Message);
            _tray.ShowBalloonTip(3500, "Ubour", ex.Message, Forms.ToolTipIcon.Error);
        }
        await Task.CompletedTask;
    }

    private void StopEngine()
    {
        _engine.Stop();
        SetStoppedState();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(silent: false);

    private async Task CheckUpdatesAsync(bool silent)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;
            var result = await _updates.CheckEngineAsync();
            UpdateInfo.Text = result.Message(_english);
            _engineUpdateUrl = result.UpdateUrl;
            OpenEngineReleaseButton.Visibility = result.UpdateUrl is null ? Visibility.Collapsed : Visibility.Visible;
            if (result.UpdateUrl is not null)
            {
                _tray.ShowBalloonTip(4000, "Ubour", result.Message(_english), Forms.ToolTipIcon.Info);
            }
            else if (!silent)
            {
                _tray.ShowBalloonTip(2500, "Ubour", result.Message(_english), Forms.ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            UpdateInfo.Text = _english ? "Update check failed: " + ex.Message : "تعذر فحص التحديث: " + ex.Message;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenEngineReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engineUpdateUrl is null) return;
        Process.Start(new ProcessStartInfo { FileName = _engineUpdateUrl, UseShellExecute = true });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _tray.ShowBalloonTip(2000, "Ubour", _english ? "Ubour is still running in the system tray." : "عبور ما زال يعمل قرب الساعة.", Forms.ToolTipIcon.Info);
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

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _english = !_english;
        ApplyLanguage();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _light = !_light;
        var colors = _light
            ? new Dictionary<string, WpfColor> { ["WindowBrush"] = WpfColor.FromRgb(244, 247, 251), ["PanelBrush"] = WpfColors.White, ["PanelSoftBrush"] = WpfColor.FromRgb(225, 232, 243), ["TextBrush"] = WpfColor.FromRgb(19, 33, 55), ["MutedBrush"] = WpfColor.FromRgb(84, 102, 130), ["AccentBrush"] = WpfColor.FromRgb(35, 169, 155), ["AccentTextBrush"] = WpfColors.White, ["DangerBrush"] = WpfColor.FromRgb(211, 61, 72) }
            : new Dictionary<string, WpfColor> { ["WindowBrush"] = WpfColor.FromRgb(11, 18, 32), ["PanelBrush"] = WpfColor.FromRgb(18, 28, 46), ["PanelSoftBrush"] = WpfColor.FromRgb(26, 39, 64), ["TextBrush"] = WpfColor.FromRgb(243, 247, 255), ["MutedBrush"] = WpfColor.FromRgb(170, 184, 208), ["AccentBrush"] = WpfColor.FromRgb(50, 198, 184), ["AccentTextBrush"] = WpfColor.FromRgb(6, 32, 29), ["DangerBrush"] = WpfColor.FromRgb(255, 107, 107) };
        foreach (var item in colors) System.Windows.Application.Current.Resources[item.Key] = new SolidColorBrush(item.Value);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        FlowDirection = _english ? System.Windows.FlowDirection.LeftToRight : System.Windows.FlowDirection.RightToLeft;
        Title = "Ubour";
        AppTitle.Text = _english ? "Ubour" : "عبور";
        AppSubtitle.Text = _english ? "Simple, transparent connectivity control" : "تحكم بسيط وواضح بالاتصال";
        LanguageButton.Content = _english ? "العربية" : "English";
        ThemeButton.Content = _english ? (_light ? "Dark mode" : "Light mode") : (_light ? "وضع داكن" : "وضع فاتح");
        OpenEngineReleaseButton.Content = _english ? "Open official update page" : "فتح صفحة التحديث الرسمي";
        InfoTitle.Text = _english ? "Application status" : "حالة البرنامج";
        EngineInfo.Text = _english ? "Connection engine: bundled official build" : "محرك التشغيل: نسخة رسمية مضمّنة";
        ExitInfo.Text = _english ? "Minimize keeps it running. Closing stops it completely." : "التصغير يبقيه يعمل. زر الإغلاق يوقفه كليًا.";
        FooterText.Text = _english ? "Version 1.0.0 · Does not change your IP address · Administrator access required" : "الإصدار 1.0.0 · لا يغيّر عنوان الإنترنت · يتطلب صلاحية مسؤول";
        SetStateTexts();
    }

    private void SetStateTexts()
    {
        if (_engine.IsRunning) SetRunningState(); else SetStoppedState();
    }

    private void SetRunningState()
    {
        StatusDot.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"];
        StatusLabel.Text = _english ? "Running" : "يعمل الآن";
        StatusDetail.Text = _english ? "The connection engine is active in the background." : "محرك الاتصال نشط في الخلفية.";
        ToggleButton.Content = _english ? "Stop connection" : "إيقاف الاتصال";
    }

    private void SetStoppedState()
    {
        StatusDot.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["DangerBrush"];
        StatusLabel.Text = _english ? "Stopped" : "متوقف";
        StatusDetail.Text = _english ? "The connection engine is not running." : "محرك الاتصال متوقف.";
        ToggleButton.Content = _english ? "Start connection" : "تشغيل الاتصال";
    }

    private void SetErrorState(string message)
    {
        StatusDot.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["DangerBrush"];
        StatusLabel.Text = _english ? "Could not start" : "تعذر التشغيل";
        StatusDetail.Text = message;
        ToggleButton.Content = _english ? "Try again" : "إعادة المحاولة";
    }
}
