using System.Windows;
using Ubour.AdBlock;

namespace Ubour;

public partial class SettingsWindow : Window
{
    private readonly bool _english;

    public string? CustomVlessUrl { get; set; }
    public string SelectedDohUrl { get; set; } = "https://cloudflare-dns.com/dns-query";

    public SettingsWindow(bool english, string? initialVlessUrl, string? initialDohUrl)
    {
        InitializeComponent();
        _english = english;
        CustomVlessUrl = initialVlessUrl;
        TxtVlessUrl.Text = initialVlessUrl ?? "";

        if (english)
        {
            FlowDirection = System.Windows.FlowDirection.LeftToRight;
            Title = "Advanced Settings - Ubour";
            SettingsTitle.Text = "Ubour Advanced Settings";
            LblFiltersSection.Text = "AdBlock & Tracking Filter Rules";
            LblFiltersDesc.Text = "Update and expand filter rules online to over 2M+ domains (AdGuard, OISD, HaGeZi, StevenBlack).";
            BtnUpdateFilters.Content = "⟳ Update Rules Online Now";
            LblVlessSection.Text = "Custom VLESS Server";
            LblVlessDesc.Text = "Enter your custom VLESS Reality link (starts with vless://):";
            LblDohSection.Text = "Secure DNS Provider (DoH)";
            LblDohDesc.Text = "Select upstream DNS for clean, non-blocked queries:";
            BtnSave.Content = "Save & Close";
        }
        else
        {
            FlowDirection = System.Windows.FlowDirection.RightToLeft;
        }

        if (!string.IsNullOrEmpty(initialDohUrl))
        {
            if (initialDohUrl.Contains("quad9")) CmbDohProvider.SelectedIndex = 1;
            else if (initialDohUrl.Contains("google")) CmbDohProvider.SelectedIndex = 2;
            else if (initialDohUrl.Contains("adguard")) CmbDohProvider.SelectedIndex = 3;
            else CmbDohProvider.SelectedIndex = 0;
        }
    }

    private async void BtnUpdateFilters_Click(object sender, RoutedEventArgs e)
    {
        BtnUpdateFilters.IsEnabled = false;
        FilterUpdateProgress.Visibility = Visibility.Visible;
        TxtUpdateStatus.Visibility = Visibility.Visible;
        TxtUpdateStatus.Text = _english ? "Downloading and parsing filter lists..." : "جارٍ تحميل وفهرسة القوائم عبر الإنترنت...";

        var progress = new Progress<int>(percent =>
        {
            FilterUpdateProgress.Value = percent;
        });

        try
        {
            var count = await AdBlockEngine.Instance.UpdateFiltersOnlineAsync(progress);
            TxtUpdateStatus.Text = _english
                ? $"Successfully updated! Total active rules: {count:N0}"
                : $"تم التحديث بنجاح! إجمالي القواعد النشطة: {count:N0}";
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = _english ? $"Update failed: {ex.Message}" : $"فشل التحديث: {ex.Message}";
        }
        finally
        {
            BtnUpdateFilters.IsEnabled = true;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CustomVlessUrl = TxtVlessUrl.Text.Trim();
        SelectedDohUrl = CmbDohProvider.SelectedIndex switch
        {
            1 => "https://dns.quad9.net/dns-query",
            2 => "https://dns.google/dns-query",
            3 => "https://dns.adguard-dns.com/dns-query",
            _ => "https://cloudflare-dns.com/dns-query"
        };
        DnsProxyServer.Instance.SetUpstreamDoh(SelectedDohUrl);
        DialogResult = true;
        Close();
    }
}
