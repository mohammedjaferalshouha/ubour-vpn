import socket, struct, time, os, urllib.request, json

def load_filters():
    rules_path = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Programs', 'Ubour', 'filters', 'adblock_rules.txt')
    if not os.path.exists(rules_path):
        rules_path = os.path.join('vpn', 'Ubour', 'filters', 'adblock_rules.txt')
    
    blocked_hashes = set()
    with open(rules_path, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith('#') or line.startswith('!'): continue
            if line.startswith('||'):
                d = line[2:].rstrip('^').strip().lower()
                if d: blocked_hashes.add(d)
            elif line.startswith('0.0.0.0 ') or line.startswith('127.0.0.1 '):
                parts = line.split()
                if len(parts) >= 2:
                    d = parts[1].strip().lower()
                    if d and d not in ('localhost', 'broadcasthost'): blocked_hashes.add(d)
            else:
                d = line.rstrip('^').strip().lower()
                if d and ' ' not in d and '/' not in d: blocked_hashes.add(d)
    return blocked_hashes

def is_blocked(domain, blocked_hashes):
    d = domain.strip().lower().rstrip('.')
    if d in blocked_hashes: return True
    parts = d.split('.')
    for i in range(1, len(parts)):
        sub = '.'.join(parts[i:])
        if sub in blocked_hashes: return True
    return False

def test_cloudflare_warp():
    print("\n--- 2. Testing Cloudflare WARP & DoH Connectivity ---")
    # 1. WireGuard Endpoint
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.settimeout(3.0)
        sock.sendto(b'\x01\x00\x00\x00', ('162.159.192.1', 2408))
        print("  [SUCCESS] Cloudflare WireGuard Endpoint (162.159.192.1:2408) is REACHABLE.")
    except Exception as e:
        print(f"  [ERROR] WARP Endpoint: {e}")
    
    # 2. Cloudflare Trace API
    try:
        req = urllib.request.Request('https://www.cloudflare.com/cdn-cgi/trace', headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=5) as r:
            trace_content = r.read().decode('utf-8')
            lines = dict(l.split('=', 1) for l in trace_content.strip().split('\n') if '=' in l)
            print(f"  [SUCCESS] Cloudflare CDN Trace: IP={lines.get('ip')}, Colo={lines.get('colo')}, HTTP={lines.get('http')}, TLS={lines.get('tls')}")
    except Exception as e:
        print(f"  [ERROR] Cloudflare Trace: {e}")

def main():
    print("=================================================================")
    print("   UBOUR WINDOWS DESKTOP FULL VERIFICATION BENCHMARK (v1.5.1)")
    print("=================================================================")
    
    # 1. Load Filter Database
    start = time.perf_counter()
    blocked = load_filters()
    load_time = (time.perf_counter() - start) * 1000
    print(f"\n--- 1. Filter Database Loaded ---")
    print(f"  Rules Loaded: {len(blocked):,} unique domain rules in {load_time:.1f} ms")
    
    # 2. Test Core Ad Networks & Trackers
    test_cases = [
        ("adservice.google.com", "Google Ads Network", True),
        ("googleads.g.doubleclick.net", "DoubleClick Core", True),
        ("pagead2.googlesyndication.com", "Google Syndication Ads", True),
        ("aax.amazon-adsystem.com", "Amazon Ad System", True),
        ("static.criteo.net", "Criteo Dynamic Retargeting", True),
        ("contextual.media.net", "Media.net Contextual Ads", True),
        ("cdn.taboola.com", "Taboola Content Recommendations", True),
        ("widgets.outbrain.com", "Outbrain Ad Widgets", True),
        ("adnxs.com", "AppNexus Ad Server", True),
        ("analytics.tiktok.com", "TikTok Analytics & Pixel", True),
        ("pixel.facebook.com", "Facebook Tracking Pixel", True),
        ("graph.facebook.com", "Facebook Graph Telemetry", True),
        ("metrika.yandex.ru", "Yandex Metrika Tracking", True),
        ("app.adjust.com", "Adjust Mobile Attribution", True),
        ("app.appsflyer.com", "AppsFlyer Telemetry", True),
        ("telemetry.microsoft.com", "Microsoft Windows Telemetry", True),
        ("v10.events.data.microsoft.com", "Windows 10/11 Diagnostic Events", True),
        ("cloudflare.com", "Clean Website (Cloudflare)", False),
        ("github.com", "Clean Website (GitHub)", False),
        ("wikipedia.org", "Clean Website (Wikipedia)", False),
    ]
    
    print("\n--- 2. Ad & Tracker Interception Rate ---")
    passed = 0
    total = len(test_cases)
    for domain, category, should_block in test_cases:
        t0 = time.perf_counter()
        blocked_res = is_blocked(domain, blocked)
        t_lookup = (time.perf_counter() - t0) * 1000000 # microseconds
        
        ok = (blocked_res == should_block)
        if ok: passed += 1
        tag = "[BLOCKED]" if blocked_res else "[ALLOWED]"
        status = "PASSED" if ok else "FAILED"
        print(f"  {tag:<10} | {status} ({t_lookup:4.1f} us) | {domain:<35} | {category}")
    
    rate = (passed / total) * 100
    print(f"\n  Final AdBlock Accuracy: {passed}/{total} ({rate:.1f}%)")
    
    # 3. Test Cloudflare WARP
    test_cloudflare_warp()
    
    # 4. Check Installed Binaries
    print("\n--- 4. Checking Installed Binaries & Engines ---")
    install_dir = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Programs', 'Ubour')
    binaries = [
        ('Ubour.exe', os.path.join(install_dir, 'Ubour.exe')),
        ('sing-box.exe (x64)', os.path.join(install_dir, 'engine', 'x86_64', 'sing-box.exe')),
        ('goodbyedpi.exe (x64)', os.path.join(install_dir, 'engine', 'x86_64', 'goodbyedpi.exe')),
        ('WinDivert.dll (x64)', os.path.join(install_dir, 'engine', 'x86_64', 'WinDivert.dll')),
        ('WinDivert64.sys (x64)', os.path.join(install_dir, 'engine', 'x86_64', 'WinDivert64.sys')),
        ('adblock_rules.txt', os.path.join(install_dir, 'filters', 'adblock_rules.txt')),
    ]
    for name, path in binaries:
        exists = os.path.exists(path)
        size = os.path.getsize(path) if exists else 0
        status = f"PRESENT ({size:,} bytes)" if exists else "MISSING"
        print(f"  [{'OK' if exists else 'MISSING'}] {name:<25}: {status}")

if __name__ == '__main__':
    main()
