using System.Windows;
using System.Windows.Media;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Ubour.Services;

public static class ThemeManager
{
    public static void ApplyTheme(ResourceDictionary resources, string theme = "dark")
    {
        if (theme == "light")
        {
            resources["BgGradientStart"] = (Color)ColorConverter.ConvertFromString("#F8FAFC");
            resources["BgGradientEnd"] = (Color)ColorConverter.ConvertFromString("#E2E8F0");
            resources["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
            resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
            resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            resources["PowerBtnOffBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
        }
        else
        {
            // Dark Modern Slate Acrylic
            resources["BgGradientStart"] = (Color)ColorConverter.ConvertFromString("#0B0F19");
            resources["BgGradientEnd"] = (Color)ColorConverter.ConvertFromString("#111827");
            resources["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
            resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#182234"));
            resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D3B55"));
            resources["TextPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            resources["TextSecondaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            resources["PowerBtnOffBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        }
    }
}
