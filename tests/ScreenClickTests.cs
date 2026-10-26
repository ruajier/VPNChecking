using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ScreenClickTests
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    private static int passed;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        passed++;
        Console.WriteLine("PASS " + message);
    }
    private static object Get(object value, string field)
    {
        return value.GetType().GetField(field, Instance).GetValue(value);
    }
    private static void Pump()
    {
        for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(5); }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var assembly = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "NetWatch.exe")));
            var alertType = assembly.GetType("NetWatch.AlertWindow", true);
            var monitorType = assembly.GetType("NetWatch.ScreenClickMonitor", true);
            var policyType = assembly.GetType("NetWatch.AlertPolicy", true);
            var message = monitorType.GetMethod("ProcessMouseMessage", Instance);
            foreach (int button in new[] { 0x0201, 0x0204, 0x0207, 0x020B })
            {
                using (var alert = (Form)Activator.CreateInstance(alertType, Instance, null,
                    new object[] { SystemIcons.Information, "这是一条测试提醒，未实际断开网络。", 5m, true }, null))
                {
                    var handle = alert.Handle;
                    int closes = 0;
                    var dismiss = (Action)Delegate.CreateDelegate(typeof(Action), alert, alertType.GetMethod("Dismiss"));
                    var monitor = Activator.CreateInstance(monitorType, Instance, null,
                        new object[] { alert, dismiss }, null);
                    alertType.GetField("screenClick", Instance).SetValue(alert, monitor);
                    var policy = Activator.CreateInstance(policyType);
                    policyType.GetMethod("Opened").Invoke(policy, null);
                    alert.FormClosed += delegate
                    {
                        closes++;
                        Check((bool)Get(monitor, "disposed"), "Listener released before FormClosed notification");
                        Check(!(bool)alertType.GetProperty("ExitRequested").GetValue(alert, null), "Screen click dismisses without requesting application exit");
                        policyType.GetMethod("Acknowledge").Invoke(policy, new object[] { 100.0, 300.0 });
                    };
                    Check(!alert.Visible && !IsWindowVisible(handle), "Alert stays hidden during message test");
                    Check((IntPtr)Get(monitor, "hook") == IntPtr.Zero, "No real global hook installed");
                    foreach (int ignored in new[] { 0x0200, 0x020A, 0x020E, 0x0202, 0x0205 })
                        message.Invoke(monitor, new object[] { ignored });
                    Pump();
                    Check(!alert.IsDisposed && closes == 0 && !(bool)Get(monitor, "pending"), "Movement scroll and button-up do not dismiss");
                    message.Invoke(monitor, new object[] { button });
                    message.Invoke(monitor, new object[] { button });
                    Check((bool)Get(monitor, "pending") && closes == 0, "Mouse-down queues close without blocking original click");
                    Pump();
                    Check(alert.IsDisposed && closes == 1, "Mouse-down dismisses exactly once: 0x" + button.ToString("X"));
                    Check((IntPtr)Get(monitor, "hook") == IntPtr.Zero && (bool)Get(monitor, "disposed"), "Dismissed alert releases listener");
                    Check((double)policyType.GetMethod("Remaining").Invoke(policy, new object[] { 100.0 }) == 300.0,
                        "Dismissal starts a full five-minute cooldown");
                    message.Invoke(monitor, new object[] { button });
                    Pump();
                    Check(closes == 1, "Messages after disposal cannot close again");
                    ((IDisposable)monitor).Dispose();
                }
            }
            using (var owner = new Form())
            {
                var handle = owner.Handle;
                int callbacks = 0;
                var monitor = Activator.CreateInstance(monitorType, Instance, null,
                    new object[] { owner, new Action(() => callbacks++) }, null);
                message.Invoke(monitor, new object[] { 0x0201 });
                ((IDisposable)monitor).Dispose();
                ((IDisposable)monitor).Dispose();
                Pump();
                Check(callbacks == 0, "Disposal cancels a queued callback and is idempotent");
                Check(!owner.Visible && !IsWindowVisible(handle), "No visible windows were opened");
            }
            using (var owner = new Form())
            {
                var handle = owner.Handle;
                int callbacks = 0;
                Func<Point, bool> ignore = point => new Rectangle(10, 10, 50, 30).Contains(point);
                var monitor = Activator.CreateInstance(monitorType, Instance, null,
                    new object[] { owner, new Action(() => callbacks++), ignore }, null);
                var handleMessage = monitorType.GetMethod("HandleMouseMessage", Instance);
                Check(handleMessage != null, "Point-aware mouse dispatch is available");
                handleMessage.Invoke(monitor, new object[] { 0x0201, (Point?)new Point(20, 20) });
                Pump();
                Check(callbacks == 0 && !(bool)Get(monitor, "pending"), "Exit button and titlebar exclusion does not queue dismissal");
                handleMessage.Invoke(monitor, new object[] { 0x0201, (Point?)new Point(200, 300) });
                Pump();
                Check(callbacks == 1 && (bool)Get(monitor, "pending"), "Screen click outside excluded controls queues dismissal");
                Check((IntPtr)Get(monitor, "hook") == IntPtr.Zero && !IsWindowVisible(handle), "Point filtering test installs no real hook or visible window");
                ((IDisposable)monitor).Dispose();
            }
            Console.WriteLine("RESULT: PASSED (" + passed + " assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error);
            return 1;
        }
    }
}
