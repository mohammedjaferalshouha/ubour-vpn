# Graph Report - antigaphity-vpn windows  (2026-08-30)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 791 nodes · 1555 edges · 48 communities (36 shown, 9 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 139 edges (avg confidence: 0.84)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `bd11a32f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .StartConnectionAsync
- UbourVpnService
- MainActivity
- Window
- Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniCreateSocket
- AdBlockEngine
- .CheckForUpdatesAsync
- ciadpi_core.c
- MainWindow
- proxy.c
- WarpManager.kt
- WarpConfig
- AppsAdapter
- SingboxManager
- Border
- extend.c
- UpdateService
- AdBlockEngine
- RadioButton
- packets.c
- Button
- desync.c
- Ubour.Services
- AppOperationMode
- AppSettings
- event_loop
- native-lib.c
- mode_add_get
- AdBlockEngineTest
- .ApplyTheme
- NativeEngine
- VpnStateManagerTest
- Ubour.Tests.csproj
- DnsManagerTests.cs
- FilterUpdateService
- BootReceiver.kt
- GoodbyeDpiManagerTests.cs
- Ubour.Models
- AdBlockDnsPacketTests.cs
- LocalizationAndSettingsTests.cs
- gradlew
- .ApplyTheme
- PowerIcon
- AdBlockEngine
- SingboxManager

## God Nodes (most connected - your core abstractions)
1. `Window` - 71 edges
2. `MainWindow` - 47 edges
3. `TextBlock` - 28 edges
4. `AdBlockEngine` - 27 edges
5. `MainActivity` - 24 edges
6. `Ubour.Services` - 20 edges
7. `SingboxManager` - 18 edges
8. `UbourVpnService` - 18 edges
9. `SingboxManager` - 18 edges
10. `Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniCreateSocket()` - 18 edges

## Surprising Connections (you probably didn't know these)
- `desync()` --calls--> `part_tls()`  [INFERRED]
  apk+ublock/app/src/main/cpp/byedpi/desync.c → apk+ublock/app/src/main/cpp/byedpi/packets.c
- `resp_error()` --calls--> `unie()`  [INFERRED]
  apk+ublock/app/src/main/cpp/byedpi/proxy.c → apk+ublock/app/src/main/cpp/byedpi/error.h
- `clear_params()` --calls--> `mem_destroy()`  [INFERRED]
  apk+ublock/app/src/main/cpp/byedpi/main.c → apk+ublock/app/src/main/cpp/byedpi/mpool.c
- `UbourApplication` --inherits--> `Application`  [EXTRACTED]
  apk+ublock/app/src/main/java/com/ubour/vpn/UbourApplication.kt → windows/src/Ubour/App.xaml
- `on_tunnel_check()` --calls--> `post_desync()`  [INFERRED]
  apk+ublock/app/src/main/cpp/byedpi/extend.c → apk+ublock/app/src/main/cpp/byedpi/desync.c

## Import Cycles
- None detected.

## Communities (48 total, 9 thin omitted)

### Community 0 - ".StartConnectionAsync"
Cohesion: 0.05
Nodes (28): ConcurrentQueue, DllImport, ExitEventArgs, IntPtr, primaryV4, primaryV6, secondaryV4, secondaryV6 (+20 more)

### Community 1 - "UbourVpnService"
Cohesion: 0.06
Nodes (21): DnsFilterServer, ByteArray, Job, CidrNode, AppOperationMode, Intent, Job, UbourVpnService (+13 more)

### Community 2 - "MainActivity"
Cohesion: 0.06
Nodes (26): ActivityMainBinding, AdapterView, AppOperationMode, ADBLOCK_ONLY, CUSTOM_VLESS, VPN_AND_ADBLOCK, VPN_ONLY, WARP_AND_ADBLOCK (+18 more)

### Community 3 - "Window"
Cohesion: 0.08
Nodes (41): LblDpiStrength, LblSettingsDns, LblSettingsLang, LblSettingsTheme, LblSettingsVless, LblStatAds, LblStatDuration, LblStatTrackers (+33 more)

### Community 4 - "Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniCreateSocket"
Cohesion: 0.13
Nodes (31): add(), clear_params(), data_from_str(), ftob(), get_addr(), get_default_ttl(), lower_char(), main() (+23 more)

### Community 5 - "AdBlockEngine"
Cohesion: 0.12
Nodes (17): CancellationTokenSource, IPEndPoint, StringBuilder, TcpListener, UdpClient, CancellationToken, HashSet, List (+9 more)

### Community 6 - ".CheckForUpdatesAsync"
Cohesion: 0.10
Nodes (23): HttpClient, HttpMessageHandler, HttpRequestMessage, HttpResponseMessage, HttpStatusCode, Task, UpdateInfo, CurrentVersion (+15 more)

### Community 7 - "ciadpi_core.c"
Cohesion: 0.12
Nodes (24): ciadpi_start(), ciadpi_stop(), client_handler(), find_tls_sni_offset(), parse_params(), send_with_dpi_bypass(), checksum(), handle_dns_udp() (+16 more)

### Community 8 - "MainWindow"
Cohesion: 0.14
Nodes (6): CancelEventArgs, DateTime, DispatcherTimer, NotifyIcon, RoutedEventArgs, MainWindow

### Community 9 - "proxy.c"
Cohesion: 0.17
Nodes (23): mod_etype(), protect(), socket_mod(), addr_equ(), auth_socks5(), create_conn(), listen_socket(), map_fix() (+15 more)

### Community 10 - "WarpManager.kt"
Cohesion: 0.15
Nodes (8): CountryManager, Curve25519KeyGen, ByteArray, Context, ServerLocation, WarpConfig, WarpManager, CountryManagerTest

### Community 11 - "WarpConfig"
Cohesion: 0.13
Nodes (16): BigInteger, host, port, privKey, pubKey, Task, WarpConfig, EndpointHost (+8 more)

### Community 12 - "AppsAdapter"
Cohesion: 0.15
Nodes (9): Adapter, AppFilterMode, ALL, EXCLUDED_ONLY, AppItem, AppsAdapter, AppViewHolder, ViewGroup (+1 more)

### Community 13 - "SingboxManager"
Cohesion: 0.31
Nodes (4): Context, WarpConfig, SingboxManager, JSONArray

### Community 14 - "Border"
Cohesion: 0.14
Nodes (14): MouseButtonEventArgs, BtnClose, BtnLang, BtnLogs, BtnMinimize, BtnPower, BtnSettings, CardModeAdBlock (+6 more)

### Community 15 - "extend.c"
Cohesion: 0.23
Nodes (15): post_desync(), check_host(), check_port(), check_proto_tcp(), on_desync(), on_desync_again(), on_fin(), on_response() (+7 more)

### Community 16 - "UpdateService"
Cohesion: 0.22
Nodes (6): FullSystemUpdateStatus, Context, UpdateInfo, UpdateService, UpstreamComponent, UpdateServiceTest

### Community 17 - "AdBlockEngine"
Cohesion: 0.31
Nodes (3): AdBlockEngine, Context, LongArray

### Community 18 - "RadioButton"
Cohesion: 0.19
Nodes (10): RbDpiAggressive, RbDpiMedium, RbDpiStable, RbModeAdBlock, RbModeVpnAdBlock, RbModeVpnOnly, RbModeWarp, RbWarpOnly (+2 more)

### Community 19 - "packets.c"
Cohesion: 0.29
Nodes (12): chello_ext_offset(), find_tls_ext_offset(), get_http_code(), is_http(), is_http_redirect(), is_tls_chello(), mod_http(), neq_tls_sid() (+4 more)

### Community 20 - "Button"
Cohesion: 0.15
Nodes (7): BtnCheckUpdate, BtnClearLogs, BtnCloseSettings, BtnCopyLogs, BtnDownloadUpdate, BtnSaveLogs, Button

### Community 21 - "desync.c"
Cohesion: 0.38
Nodes (11): delay(), desync(), desync_udp(), drop_sack(), get_family(), send_disorder(), send_fake(), send_late_oob() (+3 more)

### Community 22 - "Ubour.Services"
Cohesion: 0.18
Nodes (5): Ubour.Tests, Ubour, Ubour.Services, Dictionary, LocalizationManager

### Community 23 - "AppOperationMode"
Cohesion: 0.17
Nodes (11): AppOperationMode, ADBLOCK_ONLY, CUSTOM_VLESS, VPN_AND_ADBLOCK, VPN_ONLY, WARP_AND_ADBLOCK, VpnState, CONNECTED (+3 more)

### Community 24 - "AppSettings"
Cohesion: 0.18
Nodes (10): AppSettings, CustomVlessUrl, DpiMode, EnableAdBlock, Language, SelectedDns, SelectedMode, Theme (+2 more)

### Community 25 - "event_loop"
Cohesion: 0.27
Nodes (7): add_event(), del_event(), destroy_pool(), init_pool(), next_event(), close_conn(), event_loop()

### Community 26 - "native-lib.c"
Cohesion: 0.24
Nodes (4): unie(), JNI_OnLoad(), JavaVM, JNIEXPORT

### Community 27 - "mode_add_get"
Cohesion: 0.24
Nodes (6): connect_hook(), mode_add_get(), mem_add(), mem_delete(), mem_destroy(), mem_get()

### Community 29 - ".ApplyTheme"
Cohesion: 0.22
Nodes (6): SelectionChangedEventArgs, BtnTheme, CmbDns, CmbLanguage, CmbTheme, ComboBox

### Community 32 - "Ubour.Tests.csproj"
Cohesion: 0.38
Nodes (5): Microsoft.NET.Test.Sdk (17.13.0), xunit (2.9.3), xunit.runner.visualstudio (3.0.2), net10.0-windows, Microsoft.NET.Sdk

### Community 33 - "DnsManagerTests.cs"
Cohesion: 0.29
Nodes (4): Fact, InlineData, Theory, DnsManagerTests

### Community 34 - "FilterUpdateService"
Cohesion: 0.53
Nodes (3): FilterUpdateService, Context, UpdateResult

### Community 35 - "BootReceiver.kt"
Cohesion: 0.53
Nodes (4): BootReceiver, Context, Intent, BroadcastReceiver

### Community 36 - "GoodbyeDpiManagerTests.cs"
Cohesion: 0.40
Nodes (3): Fact, GoodbyeDpiManagerTests, PackagingVerificationTests

### Community 39 - "LocalizationAndSettingsTests.cs"
Cohesion: 0.40
Nodes (3): InlineData, Theory, LocalizationAndSettingsTests

### Community 40 - "gradlew"
Cohesion: 0.83
Nodes (3): gradlew script, die(), warn()

## Knowledge Gaps
- **59 isolated node(s):** `IsRunning`, `IsRunning`, `EndpointHost`, `EndpointPort`, `LocalIpv4` (+54 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 160 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `.StartConnectionAsync`, `Window`, `AdBlockEngine`, `Ubour.Models`, `Border`, `RadioButton`, `Button`, `AppOperationMode`, `AppSettings`, `.ApplyTheme`?**
  _High betweenness centrality (0.216) - this node is a cross-community bridge._
- **Why does `AdBlockEngine` connect `AdBlockEngine` to `.StartConnectionAsync`, `MainWindow`, `Ubour.Services`?**
  _High betweenness centrality (0.141) - this node is a cross-community bridge._
- **What connects `IsRunning`, `IsRunning`, `EndpointHost` to the rest of the system?**
  _59 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.StartConnectionAsync` be split into smaller, more focused modules?**
  _Cohesion score 0.05063291139240506 - nodes in this community are weakly interconnected._
- **Should `UbourVpnService` be split into smaller, more focused modules?**
  _Cohesion score 0.056051587301587304 - nodes in this community are weakly interconnected._
- **Should `MainActivity` be split into smaller, more focused modules?**
  _Cohesion score 0.06262626262626263 - nodes in this community are weakly interconnected._
- **Should `Window` be split into smaller, more focused modules?**
  _Cohesion score 0.08246225319396051 - nodes in this community are weakly interconnected._