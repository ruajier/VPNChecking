using System;
using System.Collections.Generic;
using Microsoft.Win32;
using NetWatch;

internal static class CoreTests
{
    private static int passed, failed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Equal(double expected, double actual, string message)
    {
        Check(Math.Abs(expected - actual) < 0.000001, message + " expected=" + expected + " actual=" + actual);
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static void Reject(Action<Settings> change)
    {
        var s = new Settings(); change(s);
        try { s.Validate(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid settings were accepted");
    }
    private static object StartupValue()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            return key == null ? null : key.GetValue(StartupRegistration.ValueName);
    }
    private static int Main()
    {
        Run("Transient network failure does not alert; recovery resets streak", delegate {
            var p = new AlertPolicy();
            Check(!p.Observe(true, true, true, 0), "Healthy sample alerted");
            Check(!p.Observe(false, true, true, 10), "One failure alerted");
            Check(!p.Observe(true, true, true, 20), "Recovery alerted");
            Check(!p.Observe(false, true, true, 30), "Failure after recovery reused old streak");
            Check(p.Observe(false, true, true, 40), "Two consecutive failures did not alert");
        });
        Run("Startup disconnected alerts on second confirmed sample", delegate {
            var p = new AlertPolicy();
            Check(!p.Observe(false, false, true, 0), "First startup sample alerted");
            Check(p.Observe(false, false, true, 10), "Second startup sample did not alert");
        });
        Run("VPN failure debounces only when Internet is confirmed", delegate {
            var p = new AlertPolicy();
            Check(!p.Observe(true, false, true, 0), "First VPN failure alerted");
            Check(p.Observe(true, false, true, 10), "Second VPN failure did not alert");
            p.ResetSamples();
            Check(!p.Observe(null, false, true, 20), "Unknown Internet triggered VPN warning");
            Check(!p.Observe(null, false, true, 30), "Unknown Internet built VPN streak");
            Check(!p.Observe(true, false, true, 40), "Unknown samples preserved VPN streak");
        });
        Run("Disabled VPN monitoring suppresses VPN failures", delegate {
            var p = new AlertPolicy();
            for (int i = 0; i < 5; i++) Check(!p.Observe(true, false, false, i * 10), "Disabled VPN alerted");
            Check(!p.Observe(false, false, false, 50), "Network first failure alerted");
            Check(p.Observe(false, false, false, 60), "Disabling VPN also disabled network alerts");
        });
        Run("An open dialog suppresses duplicate alerts indefinitely", delegate {
            var p = new AlertPolicy();
            p.Observe(false, false, true, 0);
            Check(p.Observe(false, false, true, 10), "Missing alert");
            p.Opened();
            Check(p.DialogOpen, "Dialog state missing");
            Check(!p.Observe(false, false, true, 20), "Duplicate popup");
            Check(!p.Observe(false, false, true, 100000), "Open popup expired by itself");
        });
        Run("Cooldown starts on manual close, not first detection", delegate {
            var p = new AlertPolicy();
            p.Observe(false, false, true, 0); p.Observe(false, false, true, 10); p.Opened();
            Equal(0, p.Remaining(500), "Cooldown started before close");
            p.Acknowledge(500, 300);
            Check(!p.DialogOpen, "Close did not clear dialog state");
            Equal(300, p.Remaining(500), "Cooldown did not start at close");
            Check(!p.Observe(false, false, true, 799.999), "Alert before deadline");
            Check(p.Observe(false, false, true, 800), "Persistent failure did not alert at deadline");
        });
        Run("Recovery and new outage during cooldown do not shorten or extend it", delegate {
            var p = new AlertPolicy();
            p.Acknowledge(100, 300);
            Check(!p.Observe(true, true, true, 200), "Recovery alerted");
            Check(!p.Observe(false, true, true, 250), "First new failure alerted");
            Check(!p.Observe(false, true, true, 260), "Second failure bypassed cooldown");
            Equal(140, p.Remaining(260), "New outage changed deadline");
            Check(!p.Observe(false, true, true, 399), "Alert too early");
            Check(p.Observe(false, true, true, 400), "New outage did not alert at original deadline");
        });
        Run("Recovery by cooldown end suppresses stale failure", delegate {
            var p = new AlertPolicy();
            p.Observe(false, false, true, 0); p.Observe(false, false, true, 10); p.Opened();
            p.Acknowledge(20, 300);
            Check(!p.Observe(true, true, true, 100), "Recovery alerted");
            Check(!p.Observe(true, true, true, 320), "Stale failure alerted at deadline");
            Equal(0, p.Remaining(1000), "Expired cooldown became negative");
        });
        Run("Simultaneous network and VPN outage produces one merged alert", delegate {
            var p = new AlertPolicy();
            Check(!p.Observe(false, false, true, 0), "First merged failure alerted");
            Check(p.Observe(false, false, true, 10), "Missing merged alert"); p.Opened();
            Check(!p.Observe(false, false, true, 20), "Merged fault created another popup");
            string reason = new Snapshot { NetworkOnline = false, VpnOnline = false }.FailureReason;
            Check(reason.Contains("Windows") && reason.Contains("VPN"), "Merged reason omitted a fault");
        });
        Run("Unknown results do not alert and break consecutive failures", delegate {
            var p = new AlertPolicy();
            p.Observe(false, false, true, 0);
            for (int i = 1; i <= 3; i++) Check(!p.Observe(null, null, true, i * 10), "Unknown state alerted");
            Check(!p.Observe(false, null, true, 40), "Unknown retained old network streak");
            Check(p.Observe(false, null, true, 50), "Known network failure was suppressed");
            Check(new Snapshot { NetworkOnline = null, VpnOnline = null }.FailureReason == "", "Unknown formatted as failed");
        });
        Run("ResetSamples clears debounce while retaining cooldown and dialog state", delegate {
            var p = new AlertPolicy(); p.Acknowledge(100, 300);
            p.Observe(false, false, true, 110); p.Observe(false, false, true, 120);
            p.ResetSamples(); Equal(280, p.Remaining(120), "ResetSamples cleared cooldown");
            Check(!p.Observe(false, false, true, 400), "ResetSamples did not clear streak");
            Check(p.Observe(false, false, true, 410), "Second post-reset failure did not alert");
            p.Opened(); p.ResetSamples(); Check(p.DialogOpen, "ResetSamples unexpectedly closed dialog");
        });
        Run("Defaults are valid and construction does not alter startup registry", delegate {
            object before = StartupValue();
            var s = new Settings(); s.Validate();
            Check(s.CooldownMinutes == 5m, "Default cooldown is not five minutes");
            Check(s.IntervalSeconds >= 5 && s.IntervalSeconds <= 300, "Invalid default interval");
            Check(object.Equals(before, StartupValue()), "Default settings altered startup registration");
        });
        Run("Valid settings accept documented boundaries", delegate {
            new Settings { IntervalSeconds = 5, CooldownMinutes = 0.1m, ProxyPort = 1 }.Validate();
            new Settings { IntervalSeconds = 300, CooldownMinutes = 1440m, ProxyPort = 65535 }.Validate();
            new Settings { VpnMode = "adapter", AdapterName = "Meta" }.Validate();
        });
        Run("Invalid settings are rejected", delegate {
            Reject(s => s.IntervalSeconds = 4); Reject(s => s.IntervalSeconds = 301);
            Reject(s => s.CooldownMinutes = 0); Reject(s => s.CooldownMinutes = 1440.1m);
            Reject(s => s.ProxyPort = 0); Reject(s => s.ProxyPort = 65536);
            Reject(s => s.VpnMode = "invalid"); Reject(s => s.VpnMode = null);
            Reject(s => { s.VpnMode = "adapter"; s.AdapterName = " "; });
            Reject(s => { s.VpnMode = "adapter"; s.AdapterName = null; });
        });
        Run("Proxy recognition accepts known kernels and excludes unrelated programs", delegate {
            foreach (string name in new[] { "clash", "clash-meta", "mihomo", "verge-mihomo", "xray", "sing-box", "sslocal", "shadowsocks", "naive" })
                Check(NetworkProbe.IsProxyProcess(name), "Known proxy rejected: " + name);
            foreach (string name in new[] { "explorer", "Steam", "steamwebhelper", "clash-verge", "clash-verge-service", "v2rayN", "python", "System", "not-mihomo", "", null })
                Check(!NetworkProbe.IsProxyProcess(name), "Unrelated program accepted: " + name);
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
