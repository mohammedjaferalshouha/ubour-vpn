using System.ComponentModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ubour.Models;
using Ubour.Services;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Application = System.Windows.Application;
using FlowDirection = System.Windows.FlowDirection;

namespace Ubour.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly GoodbyeDpiManager _goodbyeDpi = new();
    private readonly SingboxManager _singbox = new();
    private readonly AdBlockEngine _adBlock = new();
    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    private VpnState _currentState = VpnState.DISCONNECTED;
    private DispatcherTimer? _statsTimer;
    private DateTime _connectedTime;
    private string _baseDir = AppDomain.CurrentDomain.BaseDirectory;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        DetectBaseDirectory();

        Task.Run(() => AdBlockEngine.Initialize(_baseDir));

        SetupTrayIcon();
        ApplyLanguage(_settings.Language);
        ApplyTheme(_settings.Theme);
        SetSelectedModeUI(_settings.SelectedMode);

        SetupStatsTimer();
        SetupLogsListener();
            }

    private void DetectBaseDirectory()
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(Path.Combine(current, "engine")))
        {
            _baseDir = current;
            return;
        }

        string? parent = Directory.GetParent(current)?.Parent?.Parent?.Parent?.FullName;
        if (parent != null)
        {
            string candidate64 = Path.Combine(parent, "Ubour-windows-x64");
            if (Directory.Exists(Path.Combine(candidate64, "engine")))
            {
                _baseDir = candidate64;
                return;
            }
        }
    }

    private void SetupStatsTimer()
    {
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (s, e) =>
        {
            if (_currentState == VpnState.CONNECTED)
            {
                var elapsed = DateTime.UtcNow - _connectedTime;
                TxtStatDuration.Text = elapsed.ToString(@"hh\:mm\:ss");
                TxtStatAds.Text = AdBlockEngine.BlockedAdsCount.ToString();
                TxtStatTrackers.Text = AdBlockEngine.BlockedTrackersCount.ToString();
            }
        };
    }

    private void ApplyLanguage(string lang)
    {
        _settings.Language = lang;
        _settings.Save();

        RootGrid.FlowDirection = lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        TxtLangShort.Text = lang == "ar" ? "EN" : "عربي";

        TxtAppTitle.Text = LocalizationManager.Get("AppName", lang);

        TxtModeWarpTitle.Text = LocalizationManager.Get("ModeWarp", lang);
        TxtModeWarpDesc.Text = _settings.WarpEnableAdBlock
            ? LocalizationManager.Get("ModeWarpDescWithAds", lang)
            : LocalizationManager.Get("ModeWarpDescOnly", lang);

        LblWarpSubMode.Text = lang == "ar" ? "خيارات النفق:" : "Tunnel Options:";
        RbWarpWithAds.Content = LocalizationManager.Get("WarpOptionAdBlock", lang);
        RbWarpOnly.Content = LocalizationManager.Get("WarpOptionOnly", lang);

        TxtModeAdBlockTitle.Text = LocalizationManager.Get("ModeAdBlockOnly", lang);
        TxtModeAdBlockDesc.Text = LocalizationManager.Get("ModeAdBlockOnlyDesc", lang);

        TxtModeVpnOnlyTitle.Text = LocalizationManager.Get("ModeVpnOnly", lang);
        TxtModeVpnOnlyDesc.Text = LocalizationManager.Get("ModeVpnOnlyDesc", lang);

        TxtModeVpnAdBlockTitle.Text = LocalizationManager.Get("ModeVpnAdBlock", lang);
        TxtModeVpnAdBlockDesc.Text = LocalizationManager.Get("ModeVpnAdBlockDesc", lang);

        LblStatDuration.Text = LocalizationManager.Get("StatDuration", lang);
        LblStatAds.Text = LocalizationManager.Get("StatBlockedAds", lang);
        LblStatTrackers.Text = LocalizationManager.Get("StatBlockedTrackers", lang);

        LblDpiStrength.Text = LocalizationManager.Get("DpiStrength", lang);
        RbDpiStable.Content = LocalizationManager.Get("DpiStable", lang);
        RbDpiMedium.Content = LocalizationManager.Get("DpiMedium", lang);
        RbDpiAggressive.Content = LocalizationManager.Get("DpiAggressive", lang);

        TxtSettingsHeader.Text = LocalizationManager.Get("SettingsTitle", lang);
        LblSettingsLang.Text = LocalizationManager.Get("SettingsLanguage", lang);
        LblSettingsTheme.Text = LocalizationManager.Get("SettingsTheme", lang);
        LblSettingsDns.Text = LocalizationManager.Get("SettingsDns", lang);
        LblSettingsVless.Text = LocalizationManager.Get("SettingsVless", lang);
        BtnCloseSettings.Content = LocalizationManager.Get("SettingsClose", lang);

        TxtAppVersion.Text = LocalizationManager.Get("AppVersionLabel", lang) + "v" + UpdateManager.CurrentVersion;
        BtnCheckUpdate.Content = LocalizationManager.Get("SettingsCheckUpdate", lang);
        BtnDownloadUpdate.Content = LocalizationManager.Get("UpdateDownload", lang);

        UpdateTrayContextMenu();
        UpdateStatusUI();
    }

    private void ApplyTheme(string theme)
    {
        _settings.Theme = theme;
        _settings.Save();
        TxtThemeIcon.Text = theme == "dark" ? "☀️" : "🌙";
        ThemeManager.ApplyTheme(Application.Current.Resources, theme);
    }

    private void UpdateStatusUI()
    {
        string lang = _settings.Language;
        switch (_currentState)
        {
            case VpnState.DISCONNECTED:
                TxtStatusTitle.Text = LocalizationManager.Get("StatusDisconnected", lang);
                TxtStatusDesc.Text = lang == "ar" ? "اختر الوضع المناسب ثم اضغط زر الاتصال" : "Select an operation mode and click Connect";
                PowerGlowRing.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                PowerGlowRing.Opacity = 0.3;
                PowerIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                SetModeControlsEnabled(true);
                break;

            case VpnState.CONNECTING:
                TxtStatusTitle.Text = LocalizationManager.Get("StatusConnecting", lang);
                TxtStatusDesc.Text = lang == "ar" ? "جاري تفعيل إعدادات الشبكة والمحركات..." : "Activating network engines...";
                PowerGlowRing.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                PowerGlowRing.Opacity = 0.8;
                PowerIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                SetModeControlsEnabled(false);
                break;

            case VpnState.CONNECTED:
                TxtStatusTitle.Text = LocalizationManager.Get("StatusConnected", lang);
                TxtStatusDesc.Text = GetActiveModeStatusText(lang);
                PowerGlowRing.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                PowerGlowRing.Opacity = 0.9;
                PowerIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                SetModeControlsEnabled(false);
                break;

            case VpnState.DISCONNECTING:
                TxtStatusTitle.Text = LocalizationManager.Get("StatusDisconnecting", lang);
                TxtStatusDesc.Text = lang == "ar" ? "جاري إيقاف المحركات واستعادة الشبكة..." : "Stopping engines and restoring network...";
                PowerGlowRing.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                PowerGlowRing.Opacity = 0.6;
                PowerIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                SetModeControlsEnabled(false);
                break;
        }
    }

    private string GetActiveModeStatusText(string lang)
    {
        string dpiSuffix = _settings.DpiMode switch
        {
            "Aggressive" or "-9" => "(-9)",
            "Medium" or "Compatible" or "-5" => "(-5)",
            _ => "(-1)"
        };

        return _settings.SelectedMode switch
        {
            AppOperationMode.WARP_AND_ADBLOCK => _settings.WarpEnableAdBlock
                ? (lang == "ar" ? "نفق كلاود فلير + تصفية الإعلانات نشطة" : "Cloudflare WARP + AdBlock Active")
                : (lang == "ar" ? "نفق كلاود فلير نشط (أقصى سرعة)" : "Cloudflare WARP Active (Max Speed)"),
            AppOperationMode.ADBLOCK_ONLY => lang == "ar" ? "حظر الإعلانات محلياً (اتصال مباشر 100%)" : "AdBlock Active (100% Direct Line)",
            AppOperationMode.VPN_ONLY => lang == "ar" ? $"تجاوز الحجب GoodbyeDPI نشط {dpiSuffix}" : $"GoodbyeDPI Bypass Active {dpiSuffix}",
            AppOperationMode.VPN_AND_ADBLOCK => lang == "ar" ? $"تجاوز الحجب GoodbyeDPI {dpiSuffix} + تصفية الإعلانات نشطة" : $"GoodbyeDPI {dpiSuffix} + Local AdBlock Active",
            AppOperationMode.CUSTOM_VLESS => lang == "ar" ? "خادم VLESS مخصص + تصفية الإعلانات نشطة" : "Custom VLESS Proxy Active",
            _ => lang == "ar" ? "الاتصال نشط ومحمي" : "Connection Active"
        };
    }

    private void SetModeControlsEnabled(bool enabled)
    {
        CardModeWarp.Opacity = enabled ? 1.0 : 0.6;
        CardModeAdBlock.Opacity = enabled ? 1.0 : 0.6;
        CardModeVpnOnly.Opacity = enabled ? 1.0 : 0.6;
        CardModeVpnAdBlock.Opacity = enabled ? 1.0 : 0.6;
        WarpSubModeContainer.IsEnabled = enabled;
        DpiSubModeContainer.IsEnabled = enabled;
    }

    private void SetSelectedModeUI(AppOperationMode mode)
    {
        _settings.SelectedMode = mode;
        _settings.Save();

        RbModeWarp.IsChecked = (mode == AppOperationMode.WARP_AND_ADBLOCK);
        RbModeAdBlock.IsChecked = (mode == AppOperationMode.ADBLOCK_ONLY);
        RbModeVpnOnly.IsChecked = (mode == AppOperationMode.VPN_ONLY);
        RbModeVpnAdBlock.IsChecked = (mode == AppOperationMode.VPN_AND_ADBLOCK);

        var defaultBorder = (Brush)FindResource("CardBorderBrush");
        var activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

        CardModeWarp.BorderBrush = (mode == AppOperationMode.WARP_AND_ADBLOCK) ? activeBorder : defaultBorder;
        CardModeAdBlock.BorderBrush = (mode == AppOperationMode.ADBLOCK_ONLY) ? activeBorder : defaultBorder;
        CardModeVpnOnly.BorderBrush = (mode == AppOperationMode.VPN_ONLY) ? activeBorder : defaultBorder;
        CardModeVpnAdBlock.BorderBrush = (mode == AppOperationMode.VPN_AND_ADBLOCK) ? activeBorder : defaultBorder;

        WarpSubModeContainer.Visibility = (mode == AppOperationMode.WARP_AND_ADBLOCK)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_settings.WarpEnableAdBlock)
        {
            RbWarpWithAds.IsChecked = true;
        }
        else
        {
            RbWarpOnly.IsChecked = true;
        }

        DpiSubModeContainer.Visibility = (mode == AppOperationMode.VPN_ONLY || mode == AppOperationMode.VPN_AND_ADBLOCK)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_settings.DpiMode == "Aggressive" || _settings.DpiMode == "-9")
        {
            RbDpiAggressive.IsChecked = true;
        }
        else if (_settings.DpiMode == "Medium" || _settings.DpiMode == "Compatible" || _settings.DpiMode == "-5")
        {
            RbDpiMedium.IsChecked = true;
        }
        else
        {
            RbDpiStable.IsChecked = true;
        }
    }

    private void WarpMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.WarpEnableAdBlock = (RbWarpWithAds?.IsChecked == true);
        _settings.Save();

        string lang = _settings.Language;
        TxtModeWarpDesc.Text = _settings.WarpEnableAdBlock
            ? LocalizationManager.Get("ModeWarpDescWithAds", lang)
            : LocalizationManager.Get("ModeWarpDescOnly", lang);
    }

    private void DpiMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (RbDpiAggressive?.IsChecked == true)
        {
            _settings.DpiMode = "Aggressive";
        }
        else if (RbDpiMedium?.IsChecked == true)
        {
            _settings.DpiMode = "Medium";
        }
        else
        {
            _settings.DpiMode = "Stable";
        }
        _settings.Save();
    }

    private void CardMode_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentState == VpnState.CONNECTED || _currentState == VpnState.CONNECTING)
        {
            string notice = LocalizationManager.Get("LockNotice", _settings.Language);
            MessageBox.Show(this, notice, LocalizationManager.Get("AppName", _settings.Language), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (sender is Border border && border.Tag != null)
        {
            int modeInt = int.Parse(border.Tag.ToString()!);
            SetSelectedModeUI((AppOperationMode)modeInt);
        }
    }

    private async void BtnPower_Click(object sender, RoutedEventArgs e)
    {
        if (_currentState == VpnState.DISCONNECTED)
        {
            await StartConnectionAsync();
        }
        else if (_currentState == VpnState.CONNECTED)
        {
            await StopConnectionAsync();
        }
    }

    private async Task StartConnectionAsync()
    {
        _currentState = VpnState.CONNECTING;
        UpdateStatusUI();

        await Task.Delay(300);

        bool success = false;
        string? failureReason = null;

        try
        {
            string dnsServer = _settings.SelectedDns;
            AdBlockEngine.ResetStats();

            switch (_settings.SelectedMode)
            {
                case AppOperationMode.ADBLOCK_ONLY:
                    success = await Task.Run(() =>
                    {
                        // 1. Start DNS Filter Server (UDP + TCP)
                        bool dnsOk = _adBlock.Start(upstreamDns: dnsServer, port: 53);
                        if (!dnsOk || !_adBlock.VerifyHealth(53, 2000))
                        {
                            failureReason = "فشل فتح منفذ خادم الأسماء المحلي 53 (UDP/TCP)";
                            _adBlock.Stop();
                            return false;
                        }

                        // 2. Start sing-box Local Routing Engine
                        bool sbOk = _singbox.StartAdBlockOnly(_baseDir);
                        if (!sbOk || !_singbox.VerifyRunning(2080, 2500))
                        {
                            failureReason = "فشل تشغيل محرك التوجيه sing-box على المنفذ 2080";
                            _singbox.Stop();
                            _adBlock.Stop();
                            return false;
                        }

                        // 3. Set and Verify Local DNS
                        bool netOk = DnsManager.SetLocalDns();
                        if (!netOk)
                        {
                            failureReason = "فشل ضبط إعدادات كارت الشبكة على الخادم المحلي";
                            _singbox.Stop();
                            _adBlock.Stop();
                            DnsManager.RestoreDns();
                            return false;
                        }

                        return true;
                    });
                    break;

                case AppOperationMode.WARP_AND_ADBLOCK:
                    success = await Task.Run(async () =>
                    {
                        bool withAdBlock = _settings.WarpEnableAdBlock;
                        if (withAdBlock)
                        {
                            bool dnsOk = _adBlock.Start(upstreamDns: "1.1.1.1", port: 53);
                            if (!dnsOk || !_adBlock.VerifyHealth(53, 2000))
                            {
                                failureReason = "فشل فتح منفذ خادم الأسماء المحلي 53";
                                _adBlock.Stop();
                                return false;
                            }
                        }

                        var warpConfig = await WarpManager.GetOrRegisterConfigAsync();
                        bool sbOk = _singbox.StartWarp(_baseDir, warpConfig, enableAdBlock: withAdBlock);
                        if (!sbOk || !_singbox.VerifyRunning(2080, 2500))
                        {
                            failureReason = "فشل بدء نفق كلاود فلير ومحرك التوجيه sing-box";
                            _singbox.Stop();
                            if (withAdBlock) _adBlock.Stop();
                            return false;
                        }

                        if (withAdBlock)
                        {
                            DnsManager.SetLocalDns();
                        }
                        else
                        {
                            DnsManager.SetCustomDns("1.1.1.1");
                        }

                        return true;
                    });
                    break;

                case AppOperationMode.VPN_AND_ADBLOCK:
                    success = await Task.Run(() =>
                    {
                        bool dpiOk = _goodbyeDpi.Start(_baseDir, mode: _settings.DpiMode);
                        if (!dpiOk)
                        {
                            failureReason = "فشل تشغيل محرك فك الحجب GoodbyeDPI";
                            return false;
                        }

                        bool dnsOk = _adBlock.Start(upstreamDns: dnsServer, port: 53);
                        if (!dnsOk || !_adBlock.VerifyHealth(53, 2000))
                        {
                            failureReason = "فشل فتح منفذ خادم الأسماء المحلي 53";
                            _adBlock.Stop();
                            _goodbyeDpi.Stop();
                            return false;
                        }

                        DnsManager.SetLocalDns();
                        return true;
                    });
                    break;

                case AppOperationMode.VPN_ONLY:
                    success = await Task.Run(() =>
                    {
                        bool dpiOk = _goodbyeDpi.Start(_baseDir, mode: _settings.DpiMode);
                        if (!dpiOk)
                        {
                            failureReason = "فشل تشغيل محرك فك الحجب GoodbyeDPI";
                            return false;
                        }

                        DnsManager.SetCustomDns(dnsServer);
                        return true;
                    });
                    break;

                case AppOperationMode.CUSTOM_VLESS:
                    success = await Task.Run(() =>
                    {
                        if (string.IsNullOrWhiteSpace(_settings.CustomVlessUrl))
                        {
                            failureReason = "يرجى إدخال رابط خادم VLESS في الإعدادات أولاً";
                            return false;
                        }

                        bool dnsOk = _adBlock.Start(upstreamDns: dnsServer, port: 53);
                        if (!dnsOk || !_adBlock.VerifyHealth(53, 2000))
                        {
                            failureReason = "فشل فتح منفذ خادم الأسماء المحلي 53";
                            _adBlock.Stop();
                            return false;
                        }

                        bool sbOk = _singbox.StartVless(_baseDir, _settings.CustomVlessUrl, enableAdBlock: _settings.EnableAdBlock);
                        if (!sbOk || !_singbox.VerifyRunning(2080, 2500))
                        {
                            failureReason = "فشل تشغيل خادم VLESS ومحرك التوجيه sing-box";
                            _singbox.Stop();
                            _adBlock.Stop();
                            return false;
                        }

                        DnsManager.SetLocalDns();
                        return true;
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            success = false;
            failureReason = ex.Message;
        }

        if (success)
        {
            _connectedTime = DateTime.UtcNow;
            _currentState = VpnState.CONNECTED;
            _statsTimer?.Start();
            WatchdogService.StartWatchdog();
        }
        else
        {
            await StopConnectionAsync();
            _currentState = VpnState.DISCONNECTED;
            if (!string.IsNullOrEmpty(failureReason))
            {
                MessageBox.Show(this, failureReason, "خطأ في بدء الاتصال", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        UpdateStatusUI();
    }

    private async Task StopConnectionAsync()
    {
        _currentState = VpnState.DISCONNECTING;
        UpdateStatusUI();

        _statsTimer?.Stop();
        WatchdogService.StopWatchdog();

        await Task.Run(() =>
        {
            try { _goodbyeDpi.Stop(); } catch { }
            try { _singbox.Stop(); } catch { }
            try { _adBlock.Stop(); } catch { }
            try { DnsManager.RestoreDns(); } catch { }
            try { ProxyManager.DisableProxy(); } catch { }
        });

        _currentState = VpnState.DISCONNECTED;
        UpdateStatusUI();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        string newLang = _settings.Language == "ar" ? "en" : "ar";
        ApplyLanguage(newLang);
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        string newTheme = _settings.Theme == "dark" ? "light" : "dark";
        ApplyTheme(newTheme);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        // Populate controls with current settings
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if (item.Tag?.ToString() == _settings.Language)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in CmbTheme.Items)
        {
            if (item.Tag?.ToString() == _settings.Theme)
            {
                CmbTheme.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in CmbDns.Items)
        {
            if (item.Tag?.ToString() == _settings.SelectedDns)
            {
                CmbDns.SelectedItem = item;
                break;
            }
        }

        TxtVlessUrl.Text = _settings.CustomVlessUrl ?? "";
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        if (CmbDns.SelectedItem is ComboBoxItem dnsItem && dnsItem.Tag != null)
        {
            _settings.SelectedDns = dnsItem.Tag.ToString()!;
        }
        _settings.CustomVlessUrl = TxtVlessUrl.Text.Trim();
        _settings.Save();

        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || RootGrid == null) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            string lang = item.Tag.ToString()!;
            if (lang != _settings.Language) ApplyLanguage(lang);
        }
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || RootGrid == null) return;
        if (CmbTheme.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            string theme = item.Tag.ToString()!;
            if (theme != _settings.Theme) ApplyTheme(theme);
        }
    }

    

    

    

    

    

    private void SetupTrayIcon()
    {
        try
        {
            string icoPath = Path.Combine(_baseDir, "app.ico");
            System.Drawing.Icon? icon = null;
            if (File.Exists(icoPath))
            {
                icon = new System.Drawing.Icon(icoPath);
            }
            else
            {
                var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (streamInfo != null)
                {
                    icon = new System.Drawing.Icon(streamInfo.Stream);
                }
            }

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Shield,
                Text = "عبور | Ubour",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
            _notifyIcon.Click += (s, e) =>
            {
                if (e is System.Windows.Forms.MouseEventArgs me && me.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };

            UpdateTrayContextMenu();
        }
        catch { }
    }

    private void UpdateTrayContextMenu()
    {
        if (_notifyIcon == null) return;
        string lang = _settings?.Language ?? "ar";

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var showItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationManager.Get("TrayShow", lang));
        showItem.Click += (s, e) => RestoreFromTray();
        showItem.Font = new System.Drawing.Font(showItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var disconnectItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationManager.Get("TrayDisconnect", lang));
        disconnectItem.Click += async (s, e) =>
        {
            await StopConnectionAsync();
        };
        contextMenu.Items.Add(disconnectItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationManager.Get("TrayExit", lang));
        exitItem.Click += (s, e) => ExitApplicationCompletely();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void MinimizeToTray()
    {
        Hide();
        ShowInTaskbar = false;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
            string lang = _settings.Language;
            string title = LocalizationManager.Get("TrayMinimizedTitle", lang);
            string msg = LocalizationManager.Get("TrayMinimizedMsg", lang);
            _notifyIcon.ShowBalloonTip(2500, title, msg, System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ExitApplicationCompletely();
        base.OnClosing(e);
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ExitApplicationCompletely();
    }

    private void ExitApplicationCompletely()
    {
        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
        catch { }

        try
        {
            Hide();
        }
        catch { }

        try
        {
            WatchdogService.StopWatchdog();
            _goodbyeDpi?.Stop();
            _singbox?.Stop();
            _adBlock?.Stop();
            DnsManager.RestoreDns();
            ProxyManager.DisableProxy();
        }
        catch { }

        try
        {
            System.Windows.Application.Current.Shutdown();
        }
        catch { }

        Environment.Exit(0);
    }

    private void SetupLogsListener()
    {
        AppLogger.OnLogAdded += (line) =>
        {
            if (LogsOverlay != null && LogsOverlay.Visibility == Visibility.Visible)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TxtLogs.AppendText(line + Environment.NewLine);
                    ScrollLogs.ScrollToEnd();
                });
            }
        };
    }

    private void BtnLogs_Click(object sender, MouseButtonEventArgs e)
    {
        TxtLogs.Text = AppLogger.GetAllLogs();
        LogsOverlay.Visibility = Visibility.Visible;
        ScrollLogs.ScrollToEnd();
    }

    private void BtnCloseLogs_Click(object sender, MouseButtonEventArgs e)
    {
        LogsOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppLogger.GetAllLogs());
            MessageBox.Show(this, "تم نسخ السجل بالكامل إلى الحافظة بنجاح.", "نسخ السجل", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"فشل النسخ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSaveLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Log Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"Ubour_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (sfd.ShowDialog() == true)
            {
                AppLogger.SaveToFile(sfd.FileName);
                MessageBox.Show(this, "تم حفظ ملف السجل بنجاح.", "حفظ السجل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"فشل الحفظ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Clear();
        TxtLogs.Clear();
    }

    private string _latestDownloadUrl = UpdateManager.ReleasesPageUrl;

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")!);
            TxtUpdateStatus.Text = LocalizationManager.Get("UpdateChecking", _settings.Language);
            BtnDownloadUpdate.Visibility = Visibility.Collapsed;

            var info = await UpdateManager.CheckForUpdatesAsync();

            if (info.HasUpdate)
            {
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                TxtUpdateStatus.Text = $"{LocalizationManager.Get("UpdateAvailable", _settings.Language)} v{info.LatestVersion}";
                _latestDownloadUrl = Environment.Is64BitOperatingSystem && !string.IsNullOrEmpty(info.DownloadUrlX64)
                    ? info.DownloadUrlX64
                    : (!string.IsNullOrEmpty(info.DownloadUrlX86) ? info.DownloadUrlX86 : info.ReleaseUrl);
                BtnDownloadUpdate.Visibility = Visibility.Visible;
            }
            else if (string.IsNullOrEmpty(info.ErrorMessage))
            {
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                TxtUpdateStatus.Text = $"{LocalizationManager.Get("UpdateUpToDate", _settings.Language)} (v{UpdateManager.CurrentVersion})";
                BtnDownloadUpdate.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                TxtUpdateStatus.Text = LocalizationManager.Get("UpdateError", _settings.Language);
                BtnDownloadUpdate.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            TxtUpdateStatus.Text = LocalizationManager.Get("UpdateError", _settings.Language);
            BtnDownloadUpdate.Visibility = Visibility.Collapsed;
            AppLogger.Error($"Check update failed: {ex.Message}");
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(_latestDownloadUrl) ? UpdateManager.ReleasesPageUrl : _latestDownloadUrl;
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to open download url: {ex.Message}");
        }
    }

}