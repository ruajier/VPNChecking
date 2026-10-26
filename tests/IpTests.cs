using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using NetWatch;

internal static class IpTests
{
    private static int passed, failed;
    private static LocalAddressCandidate Candidate(string name, string ip, bool gateway = true,
        bool up = true, string description = "Intel Ethernet", NetworkInterfaceType type = NetworkInterfaceType.Ethernet)
    {
        return new LocalAddressCandidate { InterfaceName = name, Address = IPAddress.Parse(ip),
            HasGateway = gateway, IsUp = up, Description = description, InterfaceType = type };
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static int Main()
    {
        Run("Prefers active physical LAN over virtual and tunnel interfaces", delegate {
            var wlan = Candidate("WLAN", "192.168.1.8", true, true, "Intel Wi-Fi", NetworkInterfaceType.Wireless80211);
            var values = new[] { Candidate("Meta", "198.18.0.1", true, true, "Meta Tunnel"),
                Candidate("VMware Network Adapter VMnet8", "192.168.20.1", true),
                Candidate("Radmin VPN", "26.2.3.4", true), wlan };
            Check(NetworkProbe.SelectLocalAddress(values) == wlan, "Virtual or tunnel address won");
        });
        Run("Ignores disconnected and loopback adapters", delegate {
            var wlan = Candidate("WLAN", "10.0.0.9");
            Check(NetworkProbe.SelectLocalAddress(new[] { Candidate("Ethernet", "192.168.0.10", true, false),
                Candidate("Local", "127.0.0.1"), wlan }) == wlan, "Disconnected or loopback was selected");
        });
        Run("Prefers gateway within the same address family", delegate {
            var gateway = Candidate("WLAN", "192.168.1.9");
            Check(NetworkProbe.SelectLocalAddress(new[] { Candidate("Ethernet", "192.168.8.9", false), gateway }) == gateway,
                "Gateway interface did not win");
        });
        Run("IPv4 preferred; global IPv6 used when IPv4 unavailable", delegate {
            var v4 = Candidate("WLAN", "192.168.1.9");
            var v6 = Candidate("Ethernet", "2001:db8::12");
            Check(NetworkProbe.SelectLocalAddress(new[] { v6, v4 }) == v4, "IPv6 displaced IPv4");
            Check(NetworkProbe.SelectLocalAddress(new[] { Candidate("WLAN", "fe80::1"), v6 }) == v6,
                "Valid IPv6 fallback missing");
        });
        Run("Rejects unassigned, APIPA and multicast addresses", delegate {
            Check(NetworkProbe.SelectLocalAddress(new[] { Candidate("WLAN", "0.0.0.0"),
                Candidate("WLAN", "169.254.3.5"), Candidate("WLAN", "224.0.0.1"),
                Candidate("WLAN", "::"), Candidate("WLAN", "ff02::1") }) == null,
                "An unusable address was selected");
        });
        Run("No active candidate yields no assigned IP", delegate {
            Check(NetworkProbe.SelectLocalAddress(new LocalAddressCandidate[0]) == null, "Empty list selected an IP");
        });
        Run("Ordering is stable when multiple physical adapters are available", delegate {
            var a = Candidate("Ethernet", "10.1.1.3"); var b = Candidate("WLAN", "192.168.1.3");
            Check(NetworkProbe.SelectLocalAddress(new[] { b, a }) == NetworkProbe.SelectLocalAddress(new[] { a, b }),
                "Order changed with enumeration order");
        });
        Run("IP is populated even when VPN monitoring is disabled", delegate {
            var snapshot = NetworkProbe.Read(new Settings { WatchVpn = false });
            Check(!string.IsNullOrEmpty(snapshot.LocalIp), "IP field not populated");
            Check(snapshot.LocalInterface != null, "Interface field not populated");
            Console.WriteLine("LOCAL " + snapshot.LocalInterface + " = " + snapshot.LocalIp);
        });
        Console.WriteLine("RESULT " + passed + " passed; " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
