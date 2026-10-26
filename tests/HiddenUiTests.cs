using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class HiddenUiTests
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    private static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); }
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static void CheckLayout(Form form)
    {
        var handle = form.Handle;
        foreach (var control in All(form)) control.CreateControl();
        foreach (var control in All(form).Reverse()) control.PerformLayout();
        form.PerformLayout();
        foreach (var control in All(form).Where(c => c is Button || c is NumericUpDown || c is ComboBox))
        {
            Check(control.Left >= 0 && control.Top >= 0 && control.Right <= control.Parent.ClientSize.Width + 1 &&
                control.Bottom <= control.Parent.ClientSize.Height + 1, "Control fits in parent: " + control.GetType().Name + " " + control.Text);
        }
        Check(!IsWindowVisible(handle), "Layout check did not show a desktop window");
    }

    [STAThread]
    private static int Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetWatch", "settings.json");
        string before = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        var assembly = Assembly.LoadFrom(Path.Combine(root, "NetWatch.exe"));
        var type = assembly.GetType("NetWatch.MainWindow", true);
        using (var main = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { false }, null))
        {
            var tray = (NotifyIcon)Field(type, "tray").GetValue(main);
            var timer = (System.Windows.Forms.Timer)Field(type, "timer").GetValue(main);
            timer.Stop(); tray.Visible = false;
            Check(!main.Visible, "Main window never shown on desktop");
            Check(main.Text == "网络状态", "Product renamed");
            Check(!All(main).OfType<NumericUpDown>().Any(), "Advanced settings removed from main window");
            var read = (Task)type.GetMethod("CheckNow", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!read.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Check(read.IsCompleted && !read.IsFaulted, "Real local probe completed in background");
            var ip = (Label)Field(type, "ipAddress").GetValue(main);
            System.Net.IPAddress parsed;
            Check(System.Net.IPAddress.TryParse(ip.Text, out parsed) || ip.Text == "未分配", "IP display updated by actual probe");
            if (parsed != null) Check(!System.Net.IPAddress.IsLoopback(parsed), "IP is not proxy loopback");
            var dialog = (Form)Field(type, "settingsDialog").GetValue(main);
            Check(dialog.ClientSize.Width <= main.ClientSize.Width && dialog.ClientSize.Height <= main.ClientSize.Height,
                "Settings window is no larger than the compact main window");
            Check(All(dialog).OfType<NumericUpDown>().Count() == 3, "Interval cooldown and proxy settings remain available");
            Check(All(dialog).OfType<CheckBox>().Any(c => c.Text.Contains("开机")), "Startup option remains available");
            CheckLayout(main);
            CheckLayout(dialog);
            var note = All(dialog).OfType<Label>().First(c => c.Text.Contains("仅检测本机状态"));
            var noteSize = TextRenderer.MeasureText(note.Text, note.Font, new Size(note.Width, int.MaxValue), TextFormatFlags.WordBreak);
            Check(noteSize.Height <= note.Height, "Compact explanation fits without text clipping");
            var startup = All(dialog).OfType<CheckBox>().First(c => c.Text.Contains("开机"));
            Check(startup.Right <= startup.Parent.ClientSize.Width && startup.PreferredSize.Width <= startup.Width,
                "Startup checkbox remains readable");
            Check(!main.Visible && !dialog.Visible, "Layout checked without opening windows");
            var quit = tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1] as ToolStripMenuItem;
            Check(quit != null && quit.Text == "退出", "Tray exit command remains last item");
            quit.PerformClick();
            Check(!timer.Enabled && !tray.Visible, "Production tray exit stops monitoring");
        }
        string after = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        Check(before == after, "User settings unchanged by tests");
        Console.WriteLine("RESULT: PASSED");
        return 0;
    }
}
