using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

internal static class AppearanceTests
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    private static int passed;
    private static FieldInfo Field(Type type, string name)
    {
        var value = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (value == null) throw new Exception("Missing field: " + name);
        return value;
    }
    private static object Get(object value, string name) { return Field(value.GetType(), name).GetValue(value); }
    private static void Set(object value, string name, object data) { Field(value.GetType(), name).SetValue(value, data); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++; Console.WriteLine("PASS " + message);
    }
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var item in All(child)) yield return item;
        }
    }
    private static Point InMain(Control value, Control main)
    {
        var result = Point.Empty;
        for (var current = value; current != null && current != main; current = current.Parent)
            result.Offset(current.Left, current.Top);
        return result;
    }
    private static void Layout(Control root)
    {
        foreach (var control in All(root).Reverse()) control.PerformLayout();
        root.PerformLayout();
    }
    private static object FromJson(Type type, string json)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return new DataContractJsonSerializer(type).ReadObject(stream);
    }
    private static object RoundTrip(Type type, object settings)
    {
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(type);
            serializer.WriteObject(stream, settings);
            stream.Position = 0;
            return serializer.ReadObject(stream);
        }
    }
    private static void SameMonitoringValues(object settings, int interval, decimal cooldown, int port, bool watch, string mode)
    {
        Check((int)Get(settings, "IntervalSeconds") == interval && (decimal)Get(settings, "CooldownMinutes") == cooldown &&
            (int)Get(settings, "ProxyPort") == port && (bool)Get(settings, "WatchVpn") == watch &&
            (string)Get(settings, "VpnMode") == mode, "Appearance switch preserves monitoring settings");
    }
    private static void CheckButtons(Form main, Form dialog)
    {
        Layout(main); Layout(dialog);
        var theme = (Button)Get(main, "themeButton");
        var language = (Button)Get(main, "languageButton");
        foreach (var value in new[] { theme, language })
        {
            var position = InMain(value, main);
            Check(position.X >= main.ClientSize.Width * 0.65 && position.Y >= main.ClientSize.Height * 0.75,
                "Small appearance control is in bottom right: " + value.Name + " " + value.Text);
            Check(value.Width < main.ClientSize.Width * 0.15 && value.Height < main.ClientSize.Height * 0.15,
                "Appearance button remains small: " + value.Text);
            Check(position.X + value.Width <= main.ClientSize.Width && position.Y + value.Height <= main.ClientSize.Height,
                "Appearance button fits main window: " + value.Text);
        }
        var themePosition = InMain(theme, main); var languagePosition = InMain(language, main);
        Check(!new Rectangle(themePosition, theme.Size).IntersectsWith(new Rectangle(languagePosition, language.Size)),
            "Theme and language buttons do not overlap");
        foreach (var value in All(dialog).Where(c => c is Button || c is NumericUpDown || c is ComboBox))
            Check(value.Left >= 0 && value.Top >= 0 && value.Right <= value.Parent.ClientSize.Width + 1 &&
                value.Bottom <= value.Parent.ClientSize.Height + 1, "Settings control fits in selected language: " + value.Text);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string productionPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "NetWatch.exe"));
            var assembly = Assembly.LoadFrom(productionPath);
            var settingsType = assembly.GetType("NetWatch.Settings", true);
            var defaults = Activator.CreateInstance(settingsType);
            Check(!(bool)Get(defaults, "DarkMode") && (string)Get(defaults, "Language") == "zh", "New settings default to Chinese and light theme");
            var legacy = FromJson(settingsType, "{\"IntervalSeconds\":15,\"CooldownMinutes\":7,\"WatchVpn\":true,\"VpnMode\":\"proxy\",\"ProxyPort\":7897,\"AdapterName\":\"Meta\"}");
            Check(!(bool)Get(legacy, "DarkMode") && (string)Get(legacy, "Language") == "zh", "Legacy settings retain Chinese and light theme");
            SameMonitoringValues(legacy, 15, 7m, 7897, true, "proxy");
            Set(legacy, "DarkMode", true); Set(legacy, "Language", "en");
            var saved = RoundTrip(settingsType, legacy);
            Check((bool)Get(saved, "DarkMode") && (string)Get(saved, "Language") == "en", "Theme and language survive JSON round trip");
            SameMonitoringValues(saved, 15, 7m, 7897, true, "proxy");

            string settingsPath = (string)settingsType.GetProperty("FilePath", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            byte[] before = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var mainType = assembly.GetType("NetWatch.MainWindow", true);
            using (var main = (Form)Activator.CreateInstance(mainType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { false }, null))
            {
                var tray = (NotifyIcon)Get(main, "tray");
                tray.Visible = false;
                ((System.Windows.Forms.Timer)Get(main, "timer")).Stop();
                var dialog = (Form)Get(main, "settingsDialog");
                var change = mainType.GetMethod("ChangeAppearance", BindingFlags.NonPublic | BindingFlags.Instance);
                if (change == null) throw new Exception("Missing ChangeAppearance method");
                var initial = Get(main, "settings");
                int interval = (int)Get(initial, "IntervalSeconds"); decimal cooldown = (decimal)Get(initial, "CooldownMinutes");
                int port = (int)Get(initial, "ProxyPort"); bool watch = (bool)Get(initial, "WatchVpn"); string mode = (string)Get(initial, "VpnMode");
                var intervalBox = (NumericUpDown)Get(main, "interval");
                var cooldownBox = (NumericUpDown)Get(main, "cooldown");
                var portBox = (NumericUpDown)Get(main, "port");
                intervalBox.Value = interval == 300 ? 299 : interval + 1;
                cooldownBox.Value = 8.5m; portBox.Value = 10809;
                decimal unsavedInterval = intervalBox.Value;
                bool initialVpnChecked = ((CheckBox)Get(main, "vpnEnabled")).Checked;
                int initialModeIndex = ((ComboBox)Get(main, "vpnMode")).SelectedIndex;

                change.Invoke(main, new object[] { true, "en", false });
                Check(main.Text == "Network Status" && dialog.Text == "Settings", "Main and settings titles change to English");
                Check(main.BackColor.GetBrightness() < 0.25f && main.ForeColor.GetBrightness() > 0.55f,
                    "Dark theme has dark background and readable foreground");
                Check(dialog.BackColor.GetBrightness() < 0.25f, "Settings dialog follows dark theme");
                Check(All(main).OfType<Button>().Any(c => c.Text.Equals("Settings", StringComparison.OrdinalIgnoreCase)), "Main settings action is translated");
                Check(tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1].Text.Equals("Exit", StringComparison.OrdinalIgnoreCase),
                    "Tray exit action is translated");
                Check(((ComboBox)Get(main, "vpnMode")).Items.Cast<object>().All(i => !i.ToString().Contains("本地") && !i.ToString().Contains("指定")),
                    "VPN selection options are translated");
                var selectedMode = (ComboBox)Get(main, "vpnMode");
                Check(selectedMode.SelectedIndex < 0 || selectedMode.Text == selectedMode.Items[selectedMode.SelectedIndex].ToString(),
                    "Selected VPN mode display refreshes to the selected language");
                SameMonitoringValues(Get(main, "settings"), interval, cooldown, port, watch, mode);
                Check(intervalBox.Value == unsavedInterval && cooldownBox.Value == 8.5m && portBox.Value == 10809,
                    "Appearance switch preserves unsaved numeric edits");
                Check(((CheckBox)Get(main, "vpnEnabled")).Checked == initialVpnChecked && ((ComboBox)Get(main, "vpnMode")).SelectedIndex == initialModeIndex,
                    "Language switch preserves unsaved VPN selector state");
                CheckButtons(main, dialog);
                var explanation = All(dialog).OfType<Label>().First(c => c.Tag is string && ((string)c.Tag).Contains("仅检测本机状态"));
                var explanationSize = TextRenderer.MeasureText(explanation.Text, explanation.Font,
                    new Size(explanation.Width, int.MaxValue), TextFormatFlags.WordBreak);
                Check(explanationSize.Height <= explanation.Height, "English settings explanation fits in compact panel");
                foreach (var sample in new[] { new[] { "vpnState", "Service running" }, new[] { "networkState", "Disconnected" },
                    new[] { "monitorLine", "Waiting for network to settle · Checked 20:00:00" },
                    new[] { "cooldownLine", "Cooldown starts when alert closes" } })
                {
                    var label = (Label)Get(main, sample[0]);
                    var textSize = TextRenderer.MeasureText(sample[1], label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
                    Check(textSize.Width <= label.Width, "English status text fits: " + sample[0]);
                }
                var alertType = assembly.GetType("NetWatch.AlertWindow", true);
                using (var alert = (Form)Activator.CreateInstance(alertType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new object[] { SystemIcons.Information, "这是一条测试提醒，未实际断开网络。", 5m, true }, null))
                {
                    Check(alert.Text == "Network disconnected", "Alert title follows selected language");
                    Check(alert.BackColor.GetBrightness() < 0.25f, "Alert follows dark theme");
                    Check(All(alert).OfType<Button>().All(c => !c.Text.Contains("知道") && !c.Text.Contains("冷却")), "Alert acknowledgement is translated");
                    Check(!alert.Visible && !IsWindowVisible(alert.Handle), "Alert translation checked without showing popup");
                }

                change.Invoke(main, new object[] { false, "zh", false });
                Check(main.Text == "网络状态" && dialog.Text == "设置", "Chinese can be restored without restarting");
                Check(main.BackColor.GetBrightness() > 0.8f && main.ForeColor.GetBrightness() < 0.4f,
                    "Light theme restores light background and dark foreground");
                Check(tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1].Text == "退出", "Chinese tray exit restored");
                SameMonitoringValues(Get(main, "settings"), interval, cooldown, port, watch, mode);
                CheckButtons(main, dialog);
                Check(!main.Visible && !dialog.Visible && !IsWindowVisible(main.Handle) && !IsWindowVisible(dialog.Handle),
                    "All appearance checks run without displaying a window");
            }
            byte[] after = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            Check((before == null && after == null) || (before != null && after != null && before.SequenceEqual(after)),
                "Nonpersistent appearance tests do not modify user settings");
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
