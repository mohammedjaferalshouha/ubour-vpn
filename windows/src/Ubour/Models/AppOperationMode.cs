namespace Ubour.Models;

public enum AppOperationMode
{
    WARP_AND_ADBLOCK = 0, // Cloudflare WARP + AdBlock
    ADBLOCK_ONLY = 1,     // AdBlock Only (Zero Proxy, Direct DNS Filter)
    VPN_ONLY = 2,         // GoodbyeDPI -9 Bypass Only (Original Windows Launcher)
    VPN_AND_ADBLOCK = 3,  // GoodbyeDPI Bypass + Local AdBlock Filter
    CUSTOM_VLESS = 4      // VLESS Reality / Custom Proxy
}

public enum VpnState
{
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    DISCONNECTING
}
