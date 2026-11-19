using System.Collections.Generic;

namespace Ubour.Services;

public static class LocalizationManager
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["ar"] = new Dictionary<string, string>
        {
            ["AppName"] = "عبور | Ubour",
            ["AdminBadge"] = "صلاحية مسؤول ✓",
            ["StatusDisconnected"] = "جاهز للاتصال",
            ["StatusConnecting"] = "جاري الاتصال...",
            ["StatusConnected"] = "متصل وآمن",
            ["StatusDisconnecting"] = "جاري قطع الاتصال...",
            ["ModeWarp"] = "نفق كلاود فلير",
            ["ModeWarpDesc"] = "نفق مشفر كامل مع حظر الإعلانات والتعقبات",
            ["ModeWarpDescWithAds"] = "نفق مشفر كامل مع حظر الإعلانات والتعقبات",
            ["ModeWarpDescOnly"] = "نفق مشفر فائق السرعة بدون تصفية إعلانات",
            ["WarpOptionAdBlock"] = "مع مانع الإعلانات",
            ["WarpOptionOnly"] = "النفق فقط (أقصى سرعة)",
            ["WarpProtocolLabel"] = "بروتوكول الاتصال:",
            ["WarpProtocolAuto"] = "تلقائي (كالرسمي)",
            ["WarpProtocolMasque"] = "MASQUE (443)",
            ["WarpProtocolWireGuard"] = "WireGuard",
            ["MasqueReadyStatus"] = "مسار MASQUE الرسمي (منفذ 443) متاح ({0})",
            ["MasqueUnavailableStatus"] = "مسار MASQUE (443) متعذر حالياً؛ أسرع منفذ WireGuard: {0}:{1} ({2})",
            ["WarpEndpointLabel"] = "منفذ وخادم النفق (WARP):",
            ["BtnScanPorts"] = "فحص المنافذ",
            ["ScanningPorts"] = "جاري الفحص السريع...",
            ["ScanningPortsReal"] = "جاري فحص الاستجابة الحقيقية والسرعة للمنافذ...",
            ["PortScanFound"] = "تم العثور على أسرع منفذ: {0}:{1} ({2} ms) ✓",
            ["PortScanFoundSpeed"] = "أسرع منفذ مستجيب: {0}:{1} ({2}) ✓",
            ["PortScanNoneFound"] = "لم يستجب أي منفذ لبروتوكول النفق على الشبكة الحالية (تم الإبقاء على القائمة)",
            ["WarpConnectFailed"] = "تعذّر الاتصال بخادم كلاود فلير (لم تكتمل مصافحة النفق أو لا يمر الاتصال عبر المنفذ المحدد). لم يتم قطع اتصال الإنترنت الحالي.",
            ["ModeAdBlockOnly"] = "مانع الإعلانات فقط",
            ["ModeAdBlockOnlyDesc"] = "اتصال مباشر وسريع 100% مع تصفية الإعلانات",
            ["ModeVpnOnly"] = "تجاوز الحجب فقط",
            ["ModeVpnOnlyDesc"] = "فك حجب المواقع المباشر والسريع",
            ["ModeVpnAdBlock"] = "تجاوز الحجب + مانع الإعلانات",
            ["ModeVpnAdBlockDesc"] = "فك الحجب السريع مع تصفية الإعلانات محلياً",
            ["BtnConnect"] = "اتصال",
            ["BtnDisconnect"] = "قطع الاتصال",
            ["StatDuration"] = "مدة الاتصال",
            ["StatBlockedAds"] = "إعلانات محجوبة",
            ["StatBlockedTrackers"] = "تعقبات محجوبة",
            ["SettingsTitle"] = "الإعدادات العامة",
            ["SettingsLanguage"] = "اللغة:",
            ["SettingsTheme"] = "المظهر:",
            ["SettingsThemeDark"] = "المظهر المظلم",
            ["SettingsThemeLight"] = "المظهر الفاتح",
            ["SettingsDns"] = "مزود الأسماء (DNS):",
            ["SettingsVless"] = "رابط خادم VLESS مخصص:",
            ["SettingsClose"] = "إغلاق وحفظ",
            ["DpiStrength"] = "نمط فك الحجب:",
            ["DpiStable"] = "المستقر الشامل (-1)",
            ["DpiMedium"] = "المتوسط (-5)",
            ["DpiAggressive"] = "الأقصى (-9)",
            ["TrayShow"] = "فتح البرنامج",
            ["TrayDisconnect"] = "قطع الاتصال",
            ["TrayExit"] = "خروج نهائي",
            ["TrayMinimizedTitle"] = "عبور | Ubour",
            ["TrayMinimizedMsg"] = "البرنامج يعمل في الخلفية ويحمي اتصالك",
            ["LockNotice"] = "يجب قطع الاتصال أولاً قبل تبديل الوضع",
            ["SettingsCheckUpdate"] = "فحص التحديثات 🔄",
            ["UpdateChecking"] = "جاري فحص التحديثات...",
            ["UpdateUpToDate"] = "البرنامج محدث لآخر إصدار ✓",
            ["UpdateAvailable"] = "يوجد إصدار جديد متاح:",
            ["UpdateDownload"] = "تحميل التحديث ⬇",
            ["UpdateError"] = "تعذر التحقق من التحديثات",
            ["AppVersionLabel"] = "الإصدار: "
        },
        ["en"] = new Dictionary<string, string>
        {
            ["AppName"] = "Ubour | عبور",
            ["AdminBadge"] = "Administrator ✓",
            ["StatusDisconnected"] = "Ready to Connect",
            ["StatusConnecting"] = "Connecting...",
            ["StatusConnected"] = "Connected & Protected",
            ["StatusDisconnecting"] = "Disconnecting...",
            ["ModeWarp"] = "Cloudflare WARP",
            ["ModeWarpDesc"] = "Full encrypted cloud tunnel with ad & tracker blocking",
            ["ModeWarpDescWithAds"] = "Full encrypted cloud tunnel with ad & tracker blocking",
            ["ModeWarpDescOnly"] = "High-speed encrypted cloud tunnel",
            ["WarpOptionAdBlock"] = "With AdBlock",
            ["WarpOptionOnly"] = "Tunnel Only (Max Speed)",
            ["WarpProtocolLabel"] = "Protocol:",
            ["WarpProtocolAuto"] = "Auto (Official)",
            ["WarpProtocolMasque"] = "MASQUE (443)",
            ["WarpProtocolWireGuard"] = "WireGuard",
            ["MasqueReadyStatus"] = "Official MASQUE (port 443) ready ({0})",
            ["MasqueUnavailableStatus"] = "MASQUE (443) unavailable; fastest WireGuard: {0}:{1} ({2})",
            ["WarpEndpointLabel"] = "WARP Server & Port:",
            ["BtnScanPorts"] = "Scan Ports",
            ["ScanningPorts"] = "Scanning ports...",
            ["ScanningPortsReal"] = "Probing real WireGuard handshake & speed on all ports...",
            ["PortScanFound"] = "Fastest endpoint found: {0}:{1} ({2} ms) ✓",
            ["PortScanFoundSpeed"] = "Fastest active endpoint: {0}:{1} ({2}) ✓",
            ["PortScanNoneFound"] = "No WARP endpoint responded to WireGuard handshake on this network (default list kept)",
            ["WarpConnectFailed"] = "Could not connect to Cloudflare WARP (tunnel handshake or traffic check failed on the selected endpoint/port). Your existing internet connection was not disrupted.",
            ["ModeAdBlockOnly"] = "AdBlock Only",
            ["ModeAdBlockOnlyDesc"] = "100% direct line speed with local DNS filtering",
            ["ModeVpnOnly"] = "Bypass Only",
            ["ModeVpnOnlyDesc"] = "Direct and fast website bypass",
            ["ModeVpnAdBlock"] = "Bypass + AdBlock",
            ["ModeVpnAdBlockDesc"] = "GoodbyeDPI bypass combined with local ad blocking",
            ["BtnConnect"] = "Connect",
            ["BtnDisconnect"] = "Disconnect",
            ["StatDuration"] = "Duration",
            ["StatBlockedAds"] = "Blocked Ads",
            ["StatBlockedTrackers"] = "Trackers Blocked",
            ["SettingsTitle"] = "Settings",
            ["SettingsLanguage"] = "Language:",
            ["SettingsTheme"] = "Theme:",
            ["SettingsThemeDark"] = "Dark Theme",
            ["SettingsThemeLight"] = "Light Theme",
            ["SettingsDns"] = "Upstream DNS:",
            ["SettingsVless"] = "Custom VLESS URL:",
            ["SettingsClose"] = "Close & Save",
            ["DpiStrength"] = "DPI Profile:",
            ["DpiStable"] = "Stable (-1)",
            ["DpiMedium"] = "Medium (-5)",
            ["DpiAggressive"] = "Aggressive (-9)",
            ["TrayShow"] = "Open Ubour",
            ["TrayDisconnect"] = "Disconnect",
            ["TrayExit"] = "Exit",
            ["TrayMinimizedTitle"] = "Ubour | عبور",
            ["TrayMinimizedMsg"] = "Ubour is running in the background and protecting your connection",
            ["LockNotice"] = "Please disconnect before switching modes",
            ["SettingsCheckUpdate"] = "Check for Updates 🔄",
            ["UpdateChecking"] = "Checking for updates...",
            ["UpdateUpToDate"] = "Ubour is up to date ✓",
            ["UpdateAvailable"] = "New version available:",
            ["UpdateDownload"] = "Download Update ⬇",
            ["UpdateError"] = "Could not check for updates",
            ["AppVersionLabel"] = "Version: "
        }
    };

    public static string Get(string key, string lang = "ar")
    {
        if (Strings.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val))
        {
            return val;
        }
        if (Strings["ar"].TryGetValue(key, out var fallbackVal))
        {
            return fallbackVal;
        }
        return key;
    }
}
