using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NetWatch;

internal static class ProbeBench
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var settings = new Settings();
        var elapsed = new double[20];
        int networkOnline = 0, networkOffline = 0, networkUnknown = 0;
        int vpnOnline = 0, vpnOffline = 0, vpnUnknown = 0;
        Snapshot last = null;
        using (var process = Process.GetCurrentProcess())
        {
            var beforeCpu = process.TotalProcessorTime;
            for (int i = 0; i < elapsed.Length; i++)
            {
                var timer = Stopwatch.StartNew();
                last = NetworkProbe.Read(settings);
                timer.Stop(); elapsed[i] = timer.Elapsed.TotalMilliseconds;
                if (last.NetworkOnline == true) networkOnline++;
                else if (last.NetworkOnline == false) networkOffline++;
                else networkUnknown++;
                if (last.VpnOnline == true) vpnOnline++;
                else if (last.VpnOnline == false) vpnOffline++;
                else vpnUnknown++;
                Console.WriteLine("READ {0:00}: {1:F3} ms, network={2}, vpn={3}", i + 1,
                    elapsed[i], last.NetworkOnline.HasValue ? last.NetworkOnline.ToString() : "unknown",
                    last.VpnOnline.HasValue ? last.VpnOnline.ToString() : "unknown");
            }
            var afterCpu = process.TotalProcessorTime;
            double total = 0, warmTotal = 0, max = 0;
            for (int i = 0; i < elapsed.Length; i++)
            {
                total += elapsed[i]; if (i > 0) warmTotal += elapsed[i];
                if (elapsed[i] > max) max = elapsed[i];
            }
            process.Refresh();
            Console.WriteLine("SUMMARY: mean={0:F3} ms; warmMean={1:F3} ms; first={2:F3} ms; max={3:F3} ms; cpu={4:F3} ms; workingSet={5:F2} MiB",
                total / 20, warmTotal / 19, elapsed[0], max,
                (afterCpu - beforeCpu).TotalMilliseconds, process.WorkingSet64 / 1048576.0);
            Console.WriteLine("COUNTS: network online={0}, offline={1}, unknown={2}; vpn online={3}, offline={4}, unknown={5}",
                networkOnline, networkOffline, networkUnknown, vpnOnline, vpnOffline, vpnUnknown);
            Console.WriteLine("NETWORK: " + last.NetworkDetail);
            Console.WriteLine("VPN: " + last.VpnDetail);
            return networkUnknown == 0 && vpnUnknown == 0 ? 0 : 1;
        }
    }
}
