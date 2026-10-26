using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class AlertDismissTests
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
        Console.WriteLine("PASS " + message);
    }

    private static IEnumerable<Control> Children(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "NetWatch.exe"));
            var production = Assembly.LoadFrom(path);
            var alertType = production.GetType("NetWatch.AlertWindow", true);
            var policyType = production.GetType("NetWatch.AlertPolicy", true);
            var texts = production.GetType("NetWatch.UiText", true);
            var palette = production.GetType("NetWatch.Palette", true);
            var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
            var language = texts.GetField("Language", BindingFlags.Public | BindingFlags.Static);
            var dark = palette.GetField("Dark", BindingFlags.Public | BindingFlags.Static);

            foreach (string lang in new[] { "zh", "en" })
            {
                language.SetValue(null, lang);
                dark.SetValue(null, lang == "en");
                foreach (string area in new[] { "background", "title", "detail", "note", "screen-dismiss", "button", "window-close" })
                {
                    using (var alert = (Form)Activator.CreateInstance(alertType,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, new object[] { SystemIcons.Information, "这是一条测试提醒，未实际断开网络。", 5m, true }, null))
                    {
                        var handle = alert.Handle;
                        Check(!alert.Visible && !IsWindowVisible(handle), lang + " " + area + ": popup never shown");
                        var labels = Children(alert).OfType<Label>().OrderBy(c => c.Top).ToArray();
                        Check(labels.Length == 3 && Children(alert).OfType<Button>().Count() == 1,
                            lang + " " + area + ": all current client regions covered");
                        if (area == "background")
                        {
                            var button = Children(alert).OfType<Button>().Single();
                            var exitTarget = alertType.GetMethod("IsExitTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                            var center = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
                            Check((bool)exitTarget.Invoke(alert, new object[] { center }), lang + ": manual exit button is excluded from screen dismissal");
                            Check(!(bool)exitTarget.Invoke(alert, new object[] { new Point(-30000, -30000) }),
                                lang + ": unrelated screen point is allowed to dismiss");
                        }
                        var policy = Activator.CreateInstance(policyType);
                        policyType.GetMethod("Opened").Invoke(policy, null);
                        int closed = 0;
                        int exitRequests = 0;
                        const double closeTime = 123.5;
                        alert.FormClosed += delegate
                        {
                            closed++;
                            if ((bool)alertType.GetProperty("ExitRequested").GetValue(alert, null)) exitRequests++;
                            else policyType.GetMethod("Acknowledge").Invoke(policy, new object[] { closeTime, 300.0 });
                        };
                        Check((bool)policyType.GetProperty("DialogOpen").GetValue(policy, null),
                            lang + " " + area + ": cooldown not acknowledged before close");
                        Control target = area == "title" ? labels[0] : area == "detail" ? labels[1] :
                            area == "note" ? labels[2] : area == "button" ? Children(alert).OfType<Button>().Single() : (Control)alert;
                        if (area == "window-close") alert.Close();
                        else if (area == "screen-dismiss") alertType.GetMethod("Dismiss").Invoke(alert, null);
                        else onClick.Invoke(target, new object[] { EventArgs.Empty });
                        Check(alert.IsDisposed && closed == 1, lang + " " + area + ": one close disposes popup");
                        bool shouldExit = area == "button" || area == "window-close";
                        Check((bool)alertType.GetProperty("ExitRequested").GetValue(alert, null) == shouldExit,
                            lang + " " + area + ": exit intent matches the close action");
                        var remaining = policyType.GetMethod("Remaining");
                        if (shouldExit)
                        {
                            Check(exitRequests == 1, lang + " " + area + ": manual close requests application exit");
                            Check((double)remaining.Invoke(policy, new object[] { closeTime }) == 0.0,
                                lang + " " + area + ": manual exit does not start cooldown");
                            continue;
                        }
                        Check(exitRequests == 0, lang + " " + area + ": casual dismissal keeps monitoring");
                        Check(!(bool)policyType.GetProperty("DialogOpen").GetValue(policy, null),
                            lang + " " + area + ": alert no longer marked open");
                        Check((double)remaining.Invoke(policy, new object[] { closeTime }) == 300.0,
                            lang + " " + area + ": full five-minute cooldown starts at close");
                        var observe = policyType.GetMethod("Observe");
                        observe.Invoke(policy, new object[] { (bool?)false, (bool?)false, true, closeTime + 1 });
                        Check(!(bool)observe.Invoke(policy, new object[] { (bool?)false, (bool?)false, true, closeTime + 2 }),
                            lang + " " + area + ": ongoing fault suppressed during cooldown");
                        Check((bool)observe.Invoke(policy, new object[] { (bool?)false, (bool?)false, true, closeTime + 300 }),
                            lang + " " + area + ": continued monitoring alerts when cooldown expires");
                    }
                }
            }
            Console.WriteLine("RESULT: PASSED (" + assertions + " assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error);
            return 1;
        }
    }
}
