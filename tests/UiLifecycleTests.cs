using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class UiLifecycleTests
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    private static int count;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        count++; Console.WriteLine("PASS " + description);
    }
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var next in All(child)) yield return next; }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                foreach (string mode in new[] { "normal", "startup" })
                foreach (string action in new[] { "window-close", "alert-button", "alert-x", "alert-dismiss", "tray-exit" })
                {
                    var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, mode + " " + action)
                        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (var process = Process.Start(start))
                    {
                        if (!process.WaitForExit(15000)) throw new TimeoutException("Lifecycle child did not exit");
                        Console.Write(process.StandardOutput.ReadToEnd());
                        if (process.ExitCode != 0) throw new Exception("Lifecycle child failed: " + mode + " " + action);
                    }
                }
                Console.WriteLine("RESULT: PASSED (10 isolated lifecycle cases)");
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetWatch", "settings.json");
            byte[] before = File.Exists(file) ? File.ReadAllBytes(file) : null;
            object startupBefore;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                startupBefore = key == null ? null : key.GetValue("NetWatchLocal");
            var assembly = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "NetWatch.exe")));
            var type = assembly.GetType("NetWatch.MainWindow", true);
            bool startup = args[0] == "startup";
            string actionName = args[1];
            {
                string action = actionName;
                Exception failure = null;
                bool actionExecuted = false;
                using (var main = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { startup }, null))
                using (var context = new ApplicationContext())
                {
                    var timer = (System.Windows.Forms.Timer)type.GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
                    var tray = (NotifyIcon)type.GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
                    Check(timer.Enabled && tray.Visible, "Monitoring starts: " + action + " startup=" + startup);
                    tray.Visible = false;
                    var handle = main.Handle;
                    if (startup) type.GetMethod("StartHidden").Invoke(main, null);
                    main.BeginInvoke(new Action(() => {
                        try
                        {
                            if (!startup) context.MainForm = main;
                            Check(!main.Visible && !IsWindowVisible(handle), "Test never displays main window");
                            actionExecuted = true;
                            Check(!All(main).OfType<Button>().Any(c => (c.Tag as string) == "手动关闭，退出检测"),
                                "Manual exit action is not on main window");
                            var trayExit = (ToolStripMenuItem)tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1];
                            if (action == "window-close")
                            {
                                main.Close();
                                Check(!main.Visible && !main.IsDisposed && timer.Enabled,
                                    "Main X hides without stopping monitoring");
                                trayExit.PerformClick();
                            }
                            else if (action.StartsWith("alert-"))
                            {
                                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                                var alertType = assembly.GetType("NetWatch.AlertWindow", true);
                                using (var popup = (Form)Activator.CreateInstance(alertType, flags, null,
                                    new object[] { SystemIcons.Information, "这是一条测试提醒，未实际断开网络。", 5m, true }, null))
                                {
                                    var popupHandle = popup.Handle;
                                    type.GetField("alert", flags).SetValue(main, popup);
                                    var policy = type.GetField("policy", flags).GetValue(main);
                                    policy.GetType().GetMethod("Opened").Invoke(policy, null);
                                    popup.FormClosed += (FormClosedEventHandler)Delegate.CreateDelegate(typeof(FormClosedEventHandler),
                                        main, type.GetMethod("AlertClosed", flags));
                                    if (action == "alert-x") popup.Close();
                                    else if (action == "alert-button")
                                    {
                                        var button = All(popup).OfType<Button>().Single();
                                        typeof(Control).GetMethod("OnClick", flags).Invoke(button, new object[] { EventArgs.Empty });
                                    }
                                    else
                                    {
                                        alertType.GetMethod("Dismiss").Invoke(popup, null);
                                        Check(timer.Enabled && !main.IsDisposed, "Screen dismissal keeps main monitoring");
                                        double now = (double)assembly.GetType("NetWatch.NetworkProbe").GetProperty("Now").GetValue(null, null);
                                        Check((double)policy.GetType().GetMethod("Remaining").Invoke(policy, new object[] { now }) > 0,
                                            "Screen dismissal starts the production cooldown");
                                        trayExit.PerformClick();
                                    }
                                }
                            }
                            else trayExit.PerformClick();
                            Check(!timer.Enabled && !tray.Visible, "Exit stops timer and removes tray: " + action);
                        }
                        catch (Exception e) { failure = e; Application.ExitThread(); }
                    }));
                    using (var watchdog = new System.Threading.Timer(state => {
                        try { main.BeginInvoke(new Action(() => { failure = new TimeoutException("Exit did not end message loop"); Application.ExitThread(); })); }
                        catch { }
                    }, null, 5000, System.Threading.Timeout.Infinite)) Application.Run(context);
                    if (failure != null) throw failure;
                    Check(actionExecuted, "Requested exit action was executed");
                    if (!main.IsDisposed) main.Dispose();
                    Check(main.IsDisposed, "Window and resources released after message loop exits: " + action);
                }
            }
            byte[] after = File.Exists(file) ? File.ReadAllBytes(file) : null;
            Check((before == null && after == null) || (before != null && after != null && before.SequenceEqual(after)), "Settings file unchanged");
            object startupAfter;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                startupAfter = key == null ? null : key.GetValue("NetWatchLocal");
            Check(object.Equals(startupBefore, startupAfter), "Startup registry unchanged");
            Console.WriteLine("RESULT: PASSED (" + count + " assertions)");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
    }
}
