using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace NetWatch
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public int IntervalSeconds = 10;
        [DataMember] public decimal CooldownMinutes = 5;
        [DataMember] public bool WatchVpn = true;
        [DataMember] public string VpnMode = "proxy";
        [DataMember] public int ProxyPort = 7897;
        [DataMember] public string AdapterName = "Meta";
        [DataMember] public bool DarkMode;
        [DataMember] public string Language = "zh";

        [OnDeserializing]
        private void AppearanceDefaults(StreamingContext context) { Language = "zh"; }

        public static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetWatch");
        public static string FilePath { get { return Path.Combine(DirectoryPath, "settings.json"); } }

        public static Settings Load(out string warning)
        {
            warning = null;
            if (!File.Exists(FilePath)) return new Settings();
            try
            {
                using (var file = File.OpenRead(FilePath))
                {
                    var value = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(file);
                    value.Validate();
                    return value;
                }
            }
            catch (Exception e)
            {
                warning = "设置未能读取，暂用默认值：" + e.Message;
                return new Settings();
            }
        }

        public void Validate()
        {
            if (IntervalSeconds < 5 || IntervalSeconds > 300) throw new ArgumentException("检测间隔需为 5–300 秒。");
            if (CooldownMinutes < 0.1m || CooldownMinutes > 1440) throw new ArgumentException("冷却时间需为 0.1–1440 分钟。");
            if (ProxyPort < 1 || ProxyPort > 65535) throw new ArgumentException("代理端口需为 1–65535。");
            if (VpnMode != "proxy" && VpnMode != "adapter") throw new ArgumentException("VPN 检测方式无效。");
            if (VpnMode == "adapter" && string.IsNullOrWhiteSpace(AdapterName)) throw new ArgumentException("请输入目标 VPN 网卡的完整名称。");
            if (Language != "zh" && Language != "en") throw new ArgumentException("Invalid interface language.");
        }

        public void Save()
        {
            Validate();
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            using (var file = File.Create(temp))
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(file, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    // One global alert/cooldown; a clock supplied by the caller makes time changes irrelevant.
    public sealed class AlertPolicy
    {
        private int networkFailures;
        private int vpnFailures;
        private double cooldownUntil;
        public bool DialogOpen { get; private set; }

        public bool Observe(bool? network, bool? vpn, bool watchVpn, double now)
        {
            networkFailures = network == false ? Math.Min(2, networkFailures + 1) : 0;
            vpnFailures = watchVpn && network == true && vpn == false ? Math.Min(2, vpnFailures + 1) : 0;
            return !DialogOpen && Remaining(now) <= 0 && (networkFailures >= 2 || vpnFailures >= 2);
        }

        public void Opened() { DialogOpen = true; }
        public void Acknowledge(double now, double cooldownSeconds)
        {
            DialogOpen = false;
            cooldownUntil = now + cooldownSeconds;
        }
        public double Remaining(double now) { return Math.Max(0, cooldownUntil - now); }
        public void ResetSamples() { networkFailures = vpnFailures = 0; }
    }

    public sealed class Snapshot
    {
        public bool? NetworkOnline;
        public bool? VpnOnline;
        public string NetworkDetail;
        public string VpnDetail;
        public string LocalIp;
        public string LocalInterface;
        public DateTime CheckedAt;
        public string FailureReason
        {
            get
            {
                var failures = new List<string>();
                if (NetworkOnline == false) failures.Add("Windows 报告网络未连接到 Internet");
                if (VpnOnline == false) failures.Add("VPN 本机服务或目标网卡已断开");
                return string.Join("；", failures);
            }
        }
    }

    public sealed class ProxyEndpoint
    {
        public int Port;
        public int ProcessId;
        public string ProcessName;
    }

    public sealed class LocalAddressCandidate
    {
        public IPAddress Address;
        public string InterfaceName;
        public string Description;
        public NetworkInterfaceType InterfaceType;
        public bool IsUp;
        public bool HasGateway;
    }

    public static class NetworkProbe
    {
        public static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }

        public static bool? ReadInternet(out string detail)
        {
            object manager = null;
            try
            {
                manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")));
                int flags = Convert.ToInt32(manager.GetType().InvokeMember("GetConnectivity",
                    System.Reflection.BindingFlags.InvokeMethod, null, manager, null));
                bool online = (flags & 0x440) != 0;
                detail = online ? "Windows 报告已连接 Internet" : "Windows 报告无 Internet 连接";
                return online;
            }
            catch (Exception e) { detail = "无法读取 Windows 网络状态：" + e.Message; return null; }
            finally { if (manager != null && Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager); }
        }

        public static Snapshot Read(Settings settings)
        {
            var result = new Snapshot { CheckedAt = DateTime.Now };
            result.NetworkOnline = ReadInternet(out result.NetworkDetail);
            ReadLocalAddress(out result.LocalIp, out result.LocalInterface);
            if (!settings.WatchVpn) { result.VpnDetail = "VPN 监测已关闭"; return result; }
            try
            {
                if (settings.VpnMode == "adapter")
                {
                    var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                        string.Equals(n.Name, settings.AdapterName, StringComparison.OrdinalIgnoreCase));
                    result.VpnOnline = adapter != null && adapter.OperationalStatus == OperationalStatus.Up;
                    result.VpnDetail = adapter == null ? "未找到网卡：" + settings.AdapterName :
                        settings.AdapterName + (result.VpnOnline == true ? " · 已连接" : " · 已断开");
                }
                else
                {
                    var endpoint = ReadListeners(settings.ProxyPort).FirstOrDefault();
                    result.VpnOnline = endpoint != null && IsProxyProcess(endpoint.ProcessName);
                    result.VpnDetail = endpoint == null ? "127.0.0.1:" + settings.ProxyPort + " · 未监听" :
                        "127.0.0.1:" + settings.ProxyPort + " · " + endpoint.ProcessName +
                        (result.VpnOnline == true ? " 正在监听" : "（不是已知代理进程）");
                }
            }
            catch (Exception e) { result.VpnOnline = null; result.VpnDetail = "无法读取 VPN 状态：" + e.Message; }
            return result;
        }

        public static void ReadLocalAddress(out string address, out string interfaceName)
        {
            address = "未分配";
            interfaceName = "";
            try
            {
                var candidates = new List<LocalAddressCandidate>();
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        !IsLocalInterface(adapter.NetworkInterfaceType, adapter.Name, adapter.Description)) continue;
                    var properties = adapter.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(g =>
                        !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                    foreach (var unicast in properties.UnicastAddresses)
                        candidates.Add(new LocalAddressCandidate { Address = unicast.Address,
                            InterfaceName = adapter.Name, Description = adapter.Description,
                            InterfaceType = adapter.NetworkInterfaceType, IsUp = true, HasGateway = gateway });
                }
                var selected = SelectLocalAddress(candidates);
                if (selected == null) return;
                address = selected.Address.ToString();
                interfaceName = selected.InterfaceName;
            }
            catch (NetworkInformationException) { address = "读取失败"; }
            catch (System.Security.SecurityException) { address = "读取失败"; }
        }

        // Local display only: choose an active LAN interface, never the proxy's loopback or a VPN tunnel.
        public static LocalAddressCandidate SelectLocalAddress(IEnumerable<LocalAddressCandidate> candidates)
        {
            return candidates.Where(c => c != null && c.IsUp &&
                IsLocalInterface(c.InterfaceType, c.InterfaceName, c.Description) && IsUsableLocalAddress(c.Address))
                .OrderBy(c => c.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1)
                .ThenByDescending(c => c.HasGateway)
                .ThenBy(c => c.InterfaceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Address.ToString(), StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static bool IsLocalInterface(NetworkInterfaceType type, string name, string description)
        {
            if (!IsLanType(type)) return false;
            string identity = (name ?? "") + " " + (description ?? "");
            string[] virtualNames = { "virtual", "vmware", "hyper-v", "vethernet", "wintun", "wireguard",
                "tap-windows", "tun/tap", "tunnel", "clash", "mihomo", "radmin", "zerotier", "tailscale", "loopback" };
            return !virtualNames.Any(v => identity.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsLanType(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.Wireless80211 ||
                type == NetworkInterfaceType.GigabitEthernet || type == NetworkInterfaceType.FastEthernetFx ||
                type == NetworkInterfaceType.FastEthernetT;
        }

        private static bool IsUsableLocalAddress(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any)) return false;
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return bytes[0] != 0 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
            }
            return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                !address.IsIPv6LinkLocal && !address.IsIPv6Multicast;
        }

        public static bool IsProxyProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n == "clash-verge" || n == "clash-verge-service" || n == "v2rayn") return false;
            return n.StartsWith("clash") || n.StartsWith("mihomo") || n.StartsWith("verge-mihomo") ||
                n == "v2ray" || n == "xray" || n.StartsWith("sing-box") ||
                n.StartsWith("hysteria") || n == "sslocal" || n.StartsWith("shadowsocks") || n == "naive";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpRow
        {
            public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, ProcessId;
        }
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);

        // IPv4 listeners reachable at 127.0.0.1, with owning PID: an unrelated listener cannot impersonate a VPN.
        public static List<ProxyEndpoint> ReadListeners(int? onlyPort = null)
        {
            int size = 0;
            uint code = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (code != 122 && code != 0) throw new System.ComponentModel.Win32Exception((int)code);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                code = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                if (code != 0) throw new System.ComponentModel.Win32Exception((int)code);
                var values = new List<ProxyEndpoint>();
                var names = new Dictionary<int, string>();
                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf(typeof(TcpRow));
                for (int i = 0; i < count; i++)
                {
                    var row = (TcpRow)Marshal.PtrToStructure(IntPtr.Add(buffer, 4 + i * rowSize), typeof(TcpRow));
                    if (row.LocalAddress != 0 && !new IPAddress((long)row.LocalAddress).Equals(IPAddress.Loopback)) continue;
                    int port = (int)(((row.LocalPort & 255) << 8) | ((row.LocalPort >> 8) & 255));
                    if (onlyPort.HasValue && port != onlyPort.Value) continue;
                    string processName = null;
                    try
                    {
                        if (!names.TryGetValue((int)row.ProcessId, out processName))
                        {
                            using (var p = Process.GetProcessById((int)row.ProcessId)) processName = p.ProcessName;
                            names[(int)row.ProcessId] = processName;
                        }
                    }
                    catch (ArgumentException) { continue; }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    values.Add(new ProxyEndpoint { Port = port,
                        ProcessId = (int)row.ProcessId, ProcessName = processName });
                }
                return values;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public static ProxyEndpoint DetectProxy()
        {
            int[] preferred = { 7897, 7890, 10809, 10808, 1080 };
            return ReadListeners().Where(e => IsProxyProcess(e.ProcessName))
                .OrderBy(e => { int index = Array.IndexOf(preferred, e.Port); return index < 0 ? 999 : index; })
                .ThenBy(e => e.Port).FirstOrDefault();
        }
    }

    public static class StartupRegistration
    {
        public const string ValueName = "NetWatchLocal";
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(KeyPath)) return key != null && key.GetValue(ValueName) != null; }
        }
        public static void Set(bool enabled, string executable)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(ValueName, "\"" + executable + "\" --startup", RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
