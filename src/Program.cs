using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetWatch
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        internal static readonly EventWaitHandle ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NetWatchLocal.Show.v1");
        internal static readonly EventWaitHandle ExitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NetWatchLocal.Exit.v1");

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Contains("--quit")) { ExitSignal.Set(); return; }
            bool first;
            using (var mutex = new Mutex(true, @"Local\NetWatchLocal.SingleInstance.v1", out first))
            {
                if (!first)
                {
                    if (!args.Contains("--startup")) ShowSignal.Set();
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool startup = args.Contains("--startup");
                using (var window = new MainWindow(startup))
                {
                    if (startup) { window.StartHidden(); Application.Run(); }
                    else Application.Run(window);
                }
            }
        }
    }

    internal static class Palette
    {
        public static bool Dark;
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static Color Background { get { return Dark ? Color.FromArgb(24, 25, 28) : Color.FromArgb(242, 244, 247); } }
        public static Color Surface { get { return Dark ? Color.FromArgb(36, 38, 42) : Color.White; } }
        public static Color Ink { get { return Dark ? Color.FromArgb(235, 238, 242) : Color.FromArgb(28, 32, 38); } }
        public static Color Muted { get { return Dark ? Color.FromArgb(166, 175, 188) : Color.FromArgb(91, 108, 128); } }
        public static Color Green { get { return Dark ? Color.FromArgb(107, 211, 165) : Color.FromArgb(12, 126, 100); } }
        public static Color Red { get { return Dark ? Color.FromArgb(255, 139, 139) : Color.FromArgb(188, 55, 62); } }
        public static Color Amber { get { return Dark ? Color.FromArgb(231, 188, 119) : Color.FromArgb(164, 105, 29); } }
        public static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Microsoft YaHei UI", size, style); }
        public static Label Label(string text, float size = 10, Color? color = null)
        {
            return new Label { Text = UiText.T(text), Tag = text, Font = Font(size), ForeColor = color ?? Ink,
                AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        }
        public static Button Button(string text, bool primary = false)
        {
            var button = new Button { Text = UiText.T(text), Tag = text, Name = primary ? "primary" : "", Width = 160, Height = 38, Font = Font(10),
                BackColor = primary ? Ink : Surface, ForeColor = primary ? Surface : Ink,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            button.FlatAppearance.BorderColor = primary ? Ink : Color.FromArgb(210, 218, 228);
            return button;
        }

        public static void Apply(Control root)
        {
            if (root is Form)
            {
                root.BackColor = Background; root.ForeColor = Ink;
                int dark = Dark ? 1 : 0;
                DwmSetWindowAttribute(root.Handle, 20, ref dark, sizeof(int));
            }
            else if (root is Button)
            {
                var button = (Button)root;
                bool primary = button.Name == "primary";
                button.UseVisualStyleBackColor = false;
                button.BackColor = primary ? Ink : Surface; button.ForeColor = primary ? Surface : Ink;
                button.FlatAppearance.BorderColor = Dark ? Color.FromArgb(74, 80, 88) : Color.FromArgb(205, 211, 220);
                button.FlatAppearance.MouseOverBackColor = primary ? (Dark ? Color.LightGray : Color.FromArgb(55, 60, 67)) :
                    (Dark ? Color.FromArgb(55, 59, 65) : Color.FromArgb(230, 234, 240));
            }
            else if (root is Label)
            {
                Color current = root.ForeColor;
                root.ForeColor = current == Color.FromArgb(91, 108, 128) || current == Color.FromArgb(166, 175, 188) ? Muted :
                    current == Color.FromArgb(12, 126, 100) || current == Color.FromArgb(107, 211, 165) ? Green :
                    current == Color.FromArgb(188, 55, 62) || current == Color.FromArgb(255, 139, 139) ? Red :
                    current == Color.FromArgb(164, 105, 29) || current == Color.FromArgb(231, 188, 119) ? Amber : Ink;
                root.BackColor = Color.Transparent;
            }
            else if (root is CheckBox) { root.BackColor = Color.Transparent; root.ForeColor = Ink; }
            else if (root is TextBoxBase || root is NumericUpDown || root is ComboBox) { root.BackColor = Surface; root.ForeColor = Ink; }
            else if (root is Panel) root.BackColor = (root.Tag as string) == "surface" ? Surface : Color.Transparent;
            foreach (Control child in root.Controls) Apply(child);
            root.Invalidate();
        }

        public static void ApplyMenu(ContextMenuStrip menu)
        {
            menu.BackColor = Surface; menu.ForeColor = Ink;
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            foreach (ToolStripItem item in menu.Items) { item.BackColor = Surface; item.ForeColor = Ink; }
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuItemSelected { get { return Dark ? Color.FromArgb(64, 68, 74) : Color.FromArgb(220, 226, 234); } }
            public override Color MenuItemBorder { get { return Muted; } }
        }
    }

    internal sealed class MainWindow : Form
    {
        private Settings settings;
        private readonly AlertPolicy policy = new AlertPolicy();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon appIcon;
        private readonly RegisteredWaitHandle exitWait;
        private readonly RegisteredWaitHandle showWait;
        private readonly Label networkState = Palette.Label("正在检查", 17);
        private readonly Label networkDetail = Palette.Label("读取 Windows 网络状态…", 9, Palette.Muted);
        private readonly Label vpnState = Palette.Label("正在检查", 17);
        private readonly Label vpnDetail = Palette.Label("读取本地代理服务…", 9, Palette.Muted);
        private readonly Label monitorLine = Palette.Label("准备开始监测", 10);
        private readonly Label cooldownLine = Palette.Label("冷却：未开始", 10, Palette.Muted);
        private readonly Label feedback = Palette.Label("关闭窗口隐藏到托盘 · 托盘右键可退出", 9, Palette.Muted);
        private readonly Label ipAddress = Palette.Label("正在读取…", 16);
        private readonly Label ipInterface = Palette.Label("本机局域网地址", 9, Palette.Muted);
        private readonly Label settingsFeedback = Palette.Label("", 9, Palette.Muted);
        private Form settingsDialog;
        private readonly NumericUpDown interval = new NumericUpDown { Minimum = 5, Maximum = 300, Width = 95 };
        private readonly NumericUpDown cooldown = new NumericUpDown { Minimum = 0.1m, Maximum = 1440, DecimalPlaces = 1, Increment = 1, Width = 95 };
        private readonly NumericUpDown port = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 95 };
        private readonly CheckBox vpnEnabled = new CheckBox { Text = "监测 VPN", AutoSize = true };
        private readonly CheckBox autoStart = new CheckBox { Text = "开机登录后，等待联网再静默监测", AutoSize = true };
        private readonly ComboBox vpnMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 275 };
        private readonly ComboBox adapterName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 275 };
        private readonly Button detectButton = Palette.Button("自动识别", false);
        private readonly Button pauseButton = Palette.Button("暂停监测");
        private readonly ToolStripMenuItem pauseMenu = new ToolStripMenuItem("暂停监测");
        private readonly Button themeButton = Palette.Button("◐");
        private readonly Button languageButton = Palette.Button("EN");
        private readonly ToolTip appearanceTips = new ToolTip();
        private Snapshot last;
        private AlertWindow alert;
        private bool checking;
        private bool paused;
        private bool exiting;
        private bool waitingForNetwork;
        private double nextCheck;
        private double graceUntil;

        public MainWindow(bool startup)
        {
            SuspendLayout();
            waitingForNetwork = startup;
            string warning;
            settings = Settings.Load(out warning);
            UiText.Language = settings.Language;
            Palette.Dark = settings.DarkMode;
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;
            Icon = appIcon;
            Text = "网络状态";
            AutoScaleMode = AutoScaleMode.None;
            Font = Palette.Font(10);
            ForeColor = Palette.Ink;
            BackColor = Palette.Background;
            ClientSize = new Size(590, 330);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            BuildWindow();
            BuildSettingsDialog();
            LoadControls();
            using (var graphics = CreateGraphics())
            {
                float scale = graphics.DpiX / 96f;
                if (scale != 1f) Scale(new SizeF(scale, scale));
            }
            ResumeLayout(true);
            BuildTray();
            ChangeAppearance(settings.DarkMode, settings.Language, false);
            if (warning != null) feedback.Text = UiText.T(warning);
            NetworkInterfaceEvents();
            timer.Tick += Tick;
            timer.Start();
            exitWait = ThreadPool.RegisterWaitForSingleObject(Program.ExitSignal,
                (state, timedOut) => OnUi(ExitApp), null, Timeout.Infinite, false);
            showWait = ThreadPool.RegisterWaitForSingleObject(Program.ShowSignal,
                (state, timedOut) => OnUi(ShowMain), null, Timeout.Infinite, false);
            Shown += async (s, e) => await CheckNow();
        }

        public void StartHidden()
        {
            var handle = Handle;
            ShowInTaskbar = false;
            BeginInvoke(new Action(async () => await CheckNow()));
        }

        private void BuildWindow()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            foreach (int height in new[] { 100, 72, 30, 48, 40 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(root);
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.Controls.Add(StatusCard("网络", networkState, networkDetail), 0, 0);
            cards.Controls.Add(StatusCard("VPN", vpnState, vpnDetail), 1, 0); root.Controls.Add(cards, 0, 0);

            var ipRow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Palette.Surface, Tag = "surface", ColumnCount = 2,
                Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 8) };
            ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84)); ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ipRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); ipRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ipRow.Controls.Add(Palette.Label("本机 IP", 10, Palette.Muted), 0, 0);
            ipAddress.AutoEllipsis = true; ipInterface.AutoEllipsis = true;
            ipRow.Controls.Add(ipAddress, 1, 0); ipRow.Controls.Add(ipInterface, 1, 1); root.Controls.Add(ipRow, 0, 1);

            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            monitorLine.Font = cooldownLine.Font = Palette.Font(9);
            status.Controls.Add(monitorLine, 0, 0); status.Controls.Add(cooldownLine, 1, 0); root.Controls.Add(status, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            var options = Palette.Button("设置", true); options.Click += (s, e) => { LoadControls(); settingsFeedback.Text = ""; settingsDialog.ShowDialog(this); };
            var test = Palette.Button("测试提醒"); test.Click += (s, e) => OpenAlert(true);
            pauseButton.Click += (s, e) => TogglePause();
            foreach (var button in new[] { options, test, pauseButton }) { button.Width = 173; actions.Controls.Add(button); }
            root.Controls.Add(actions, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            feedback.AutoEllipsis = true; feedback.Font = Palette.Font(8); feedback.Padding = new Padding(0, 4, 0, 0);
            footer.Controls.Add(feedback, 0, 0);
            themeButton.Dock = languageButton.Dock = DockStyle.Fill;
            themeButton.Margin = languageButton.Margin = new Padding(3, 7, 0, 7);
            themeButton.Font = languageButton.Font = Palette.Font(9);
            themeButton.Click += (s, e) => ChangeAppearance(!settings.DarkMode, settings.Language, true);
            languageButton.Click += (s, e) => ChangeAppearance(settings.DarkMode, settings.Language == "en" ? "zh" : "en", true);
            footer.Controls.Add(themeButton, 1, 0); footer.Controls.Add(languageButton, 2, 0); root.Controls.Add(footer, 0, 4);
        }

        private void BuildSettingsDialog()
        {
            settingsDialog = new Form { Text = "设置", Icon = appIcon, Font = Palette.Font(9), BackColor = Palette.Background,
                AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(450, 315), FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent };
            settingsDialog.SuspendLayout();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
            foreach (int height in new[] { 224, 27, 40 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            settingsDialog.Controls.Add(root);
            var settingsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7,
                BackColor = Palette.Surface, Tag = "surface", Padding = new Padding(10, 4, 10, 4), Margin = Padding.Empty };
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 30, 30, 30, 30, 30, 30, 34 }) settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            settingsPanel.Controls.Add(Palette.Label("检测间隔 / 秒", 9), 0, 0); settingsPanel.Controls.Add(interval, 1, 0);
            settingsPanel.Controls.Add(Palette.Label("冷却 / 分钟", 9), 0, 1); settingsPanel.Controls.Add(cooldown, 1, 1);
            settingsPanel.Controls.Add(vpnEnabled, 0, 2); settingsPanel.Controls.Add(vpnMode, 1, 2);
            settingsPanel.Controls.Add(Palette.Label("代理端口", 9), 0, 3);
            var portRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            port.Width = 100; port.Dock = DockStyle.None; detectButton.Width = 100; detectButton.Height = 25; detectButton.Margin = new Padding(8, 2, 0, 0);
            portRow.Controls.Add(port); portRow.Controls.Add(detectButton); settingsPanel.Controls.Add(portRow, 1, 3);
            settingsPanel.Controls.Add(Palette.Label("VPN 网卡", 9), 0, 4); settingsPanel.Controls.Add(adapterName, 1, 4);
            settingsPanel.Controls.Add(autoStart, 0, 5); settingsPanel.SetColumnSpan(autoStart, 2);
            var limitation = Palette.Label("仅检测本机状态；IP 为局域网地址。\n本地代理正常不代表远端节点可用。", 8, Palette.Muted);
            settingsPanel.Controls.Add(limitation, 0, 6); settingsPanel.SetColumnSpan(limitation, 2); root.Controls.Add(settingsPanel, 0, 0);
            foreach (Control control in new Control[] { interval, cooldown, port, vpnEnabled, autoStart, vpnMode, adapterName })
            {
                control.Margin = new Padding(0, 3, 4, 2);
                control.Font = Palette.Font(9);
            }
            detectButton.Font = Palette.Font(9);
            interval.Dock = cooldown.Dock = vpnMode.Dock = adapterName.Dock = DockStyle.Fill;
            vpnMode.Items.AddRange(new object[] { "本地代理（Clash / v2rayN 等）", "指定 VPN 网卡" });
            foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) adapterName.Items.Add(adapter.Name);
            vpnMode.SelectedIndexChanged += (s, e) => RefreshEnabled();
            vpnEnabled.CheckedChanged += (s, e) => RefreshEnabled();
            detectButton.Click += DetectProxy;

            settingsFeedback.AutoEllipsis = true; root.Controls.Add(settingsFeedback, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
            var save = Palette.Button("保存", true); save.Width = 84; save.Height = 30; save.Font = Palette.Font(9); save.Click += SaveSettings;
            var cancel = Palette.Button("取消"); cancel.Width = 84; cancel.Height = 30; cancel.Font = Palette.Font(9); cancel.Click += (s, e) => settingsDialog.Hide();
            actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2);
            settingsDialog.AcceptButton = save; settingsDialog.CancelButton = cancel;
            using (var graphics = settingsDialog.CreateGraphics())
            {
                float scale = graphics.DpiX / 96f;
                if (scale != 1f) settingsDialog.Scale(new SizeF(scale, scale));
            }
            settingsDialog.ResumeLayout(true);
            settingsDialog.FormClosing += (s, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; settingsDialog.Hide(); } };
        }

        private Panel StatusCard(string title, Label state, Label detail)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Surface, Tag = "surface", Padding = new Padding(14, 7, 10, 7), Margin = new Padding(0, 0, 8, 0) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 19)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.Controls.Add(Palette.Label(title, 9, Palette.Muted), 0, 0); table.Controls.Add(state, 0, 1);
            detail.AutoEllipsis = true; table.Controls.Add(detail, 0, 2); panel.Controls.Add(table); return panel;
        }

        private void LoadControls()
        {
            interval.Value = settings.IntervalSeconds; cooldown.Value = settings.CooldownMinutes;
            vpnEnabled.Checked = settings.WatchVpn; vpnMode.SelectedIndex = settings.VpnMode == "adapter" ? 1 : 0;
            port.Value = settings.ProxyPort; adapterName.Text = settings.AdapterName;
            autoStart.Checked = StartupRegistration.Enabled;
            RefreshEnabled();
        }

        private void RefreshEnabled()
        {
            vpnMode.Enabled = vpnEnabled.Checked;
            port.Enabled = detectButton.Enabled = vpnEnabled.Checked && vpnMode.SelectedIndex == 0;
            adapterName.Enabled = vpnEnabled.Checked && vpnMode.SelectedIndex == 1;
        }

        private void ChangeAppearance(bool dark, string language, bool persist)
        {
            bool previousDark = settings.DarkMode;
            string previousLanguage = settings.Language;
            settings.DarkMode = dark; settings.Language = language;
            if (persist)
            {
                try { settings.Save(); }
                catch (Exception ex)
                {
                    settings.DarkMode = previousDark; settings.Language = previousLanguage;
                    feedback.Text = UiText.T("外观设置保存失败：") + ex.Message;
                    return;
                }
            }
            UiText.Language = language; Palette.Dark = dark;
            foreach (var label in new[] { networkState, networkDetail, vpnState, vpnDetail, monitorLine, cooldownLine,
                feedback, ipAddress, ipInterface, settingsFeedback }) label.Tag = null;
            pauseButton.Tag = themeButton.Tag = languageButton.Tag = null;
            UiText.Apply(this); UiText.Apply(settingsDialog);
            Palette.Apply(this); Palette.Apply(settingsDialog);
            int selectedMode = vpnMode.SelectedIndex;
            vpnMode.BeginUpdate();
            vpnMode.Items[0] = UiText.T("本地代理（Clash / v2rayN 等）");
            vpnMode.Items[1] = UiText.T("指定 VPN 网卡");
            vpnMode.SelectedIndex = -1;
            vpnMode.SelectedIndex = selectedMode;
            vpnMode.EndUpdate();
            languageButton.Text = language == "en" ? "中" : "EN";
            themeButton.AccessibleName = UiText.T("切换黑白配色");
            languageButton.AccessibleName = UiText.T("切换中英文");
            appearanceTips.SetToolTip(themeButton, themeButton.AccessibleName);
            appearanceTips.SetToolTip(languageButton, languageButton.AccessibleName);
            foreach (ToolStripItem item in tray.ContextMenuStrip.Items)
            {
                if (item is ToolStripSeparator) continue;
                if (item.Tag == null) item.Tag = item.Text;
                item.Text = UiText.T((string)item.Tag);
            }
            Palette.ApplyMenu(tray.ContextMenuStrip);
            feedback.Text = UiText.T("关闭窗口隐藏到托盘 · 托盘右键可退出");
            settingsFeedback.Text = "";
            if (alert != null && !alert.IsDisposed) { UiText.Apply(alert); Palette.Apply(alert); }
            UpdateSnapshot(); UpdateFooter();
        }

        private void UpdateSnapshot()
        {
            if (last == null)
            {
                networkState.Text = vpnState.Text = UiText.T("正在检查");
                networkDetail.Text = UiText.T("读取 Windows 网络状态…"); vpnDetail.Text = UiText.T("读取本地代理服务…");
                ipAddress.Text = UiText.T("正在读取…"); ipInterface.Text = UiText.T("本机局域网地址");
                return;
            }
            SetStatus(networkState, last.NetworkOnline, "已连接", "网络已断开");
            networkDetail.Text = UiText.T(last.NetworkDetail);
            SetStatus(vpnState, last.VpnOnline, "本地服务正常", "已断开");
            if (!settings.WatchVpn) { vpnState.Text = UiText.T("未启用"); vpnState.ForeColor = Palette.Muted; }
            if (settings.VpnMode == "adapter" && last.VpnOnline == true) vpnState.Text = UiText.T("网卡已连接");
            vpnDetail.Text = UiText.T(last.VpnDetail);
            ipAddress.Text = UiText.T(last.LocalIp);
            ipInterface.Text = string.IsNullOrEmpty(last.LocalInterface) ? UiText.T("本机局域网地址") : last.LocalInterface + UiText.T(" · 局域网地址");
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (s, e) => ShowMain());
            pauseMenu.Click += (s, e) => TogglePause(); menu.Items.Add(pauseMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());
            tray.Icon = appIcon; tray.Text = "网络状态 · 正在检查"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowMain();
        }

        private void NetworkInterfaceEvents()
        {
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += NetworkChanged;
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
        }
        private void NetworkChanged(object sender, EventArgs e) { OnUi(() => nextCheck = 0); }
        private void AvailabilityChanged(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) { NetworkChanged(sender, e); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) OnUi(() => { graceUntil = NetworkProbe.Now + 20; policy.ResetSamples(); nextCheck = 0; });
        }
        private void OnUi(Action action)
        {
            if (exiting || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private async void Tick(object sender, EventArgs e)
        {
            if (exiting) return;
            UpdateFooter();
            if (!paused && !checking && NetworkProbe.Now >= nextCheck) await CheckNow();
        }

        private async Task CheckNow()
        {
            if (checking || paused || exiting) return;
            checking = true;
            Settings sampleSettings = settings;
            try
            {
                var snapshot = await Task.Run(() => NetworkProbe.Read(sampleSettings));
                if (exiting || sampleSettings != settings) return;
                last = snapshot;
                UpdateSnapshot();
                if (waitingForNetwork)
                {
                    if (snapshot.NetworkOnline != true) return;
                    waitingForNetwork = false;
                    graceUntil = NetworkProbe.Now + 30; // Let a startup VPN finish launching.
                    policy.ResetSamples();
                }
                if (NetworkProbe.Now < graceUntil) return;
                if (policy.Observe(snapshot.NetworkOnline, snapshot.VpnOnline, settings.WatchVpn, NetworkProbe.Now)) OpenAlert(false);
            }
            catch (Exception e) { feedback.Text = UiText.T("检测暂时失败：") + e.Message; }
            finally
            {
                checking = false;
                nextCheck = NetworkProbe.Now + settings.IntervalSeconds;
                UpdateFooter();
            }
        }

        private static void SetStatus(Label label, bool? value, string up, string down)
        {
            label.Text = UiText.T(value == true ? up : value == false ? down : "状态未知");
            label.ForeColor = value == true ? Palette.Green : value == false ? Palette.Red : Palette.Amber;
        }

        private void UpdateFooter()
        {
            if (exiting || IsDisposed) return;
            double remaining = policy.Remaining(NetworkProbe.Now);
            var span = TimeSpan.FromSeconds(Math.Ceiling(remaining));
            cooldownLine.Text = policy.DialogOpen ? UiText.T("冷却：关闭弹窗后开始") : remaining > 0 ?
                UiText.T("冷却剩余 ") + ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") : UiText.T("冷却：未开始");
            string state = paused ? "已暂停" : waitingForNetwork ? "等待联网后开始监测" : NetworkProbe.Now < graceUntil ? "联网 / 唤醒准备中" : "监测中";
            monitorLine.Text = UiText.T(state) + (last == null ? "" : " · " + UiText.T("最近检测") + " " + last.CheckedAt.ToString("HH:mm:ss"));
            pauseButton.Text = pauseMenu.Text = UiText.T(paused ? "继续监测" : "暂停监测");
            string status = paused ? "已暂停" : last == null ? "正在检查" :
                last.NetworkOnline == false ? "网络断开" : settings.WatchVpn && last.VpnOnline == false ? "VPN 断开" :
                last.NetworkOnline == null || (settings.WatchVpn && last.VpnOnline == null) ? "状态未知" : "连接正常";
            tray.Text = UiText.T("网络状态") + " · " + UiText.T(status);
        }

        private void OpenAlert(bool test)
        {
            if (exiting) return;
            if (alert != null) { alert.Activate(); return; }
            policy.Opened();
            alert = new AlertWindow(appIcon, test ? "这是一条测试提醒，未实际断开网络。" : last.FailureReason,
                settings.CooldownMinutes, test);
            alert.FormClosed += AlertClosed;
            alert.Show();
            UpdateFooter();
        }

        private void AlertClosed(object sender, FormClosedEventArgs e)
        {
            var closed = (AlertWindow)sender;
            bool exitRequested = closed.ExitRequested;
            if (ReferenceEquals(alert, closed)) alert = null;
            closed.Dispose();
            if (exiting) return;
            if (exitRequested) ExitApp();
            else { policy.Acknowledge(NetworkProbe.Now, (double)settings.CooldownMinutes * 60); UpdateFooter(); }
        }

        private async void SaveSettings(object sender, EventArgs e)
        {
            var value = new Settings { IntervalSeconds = (int)interval.Value, CooldownMinutes = cooldown.Value,
                WatchVpn = vpnEnabled.Checked, VpnMode = vpnMode.SelectedIndex == 1 ? "adapter" : "proxy",
                ProxyPort = (int)port.Value, AdapterName = adapterName.Text.Trim(), DarkMode = settings.DarkMode, Language = settings.Language };
            try
            {
                value.Validate(); value.Save();
                settings = value; policy.ResetSamples(); nextCheck = 0;
                feedback.ForeColor = Palette.Green;
                feedback.Text = UiText.T("设置已保存。连续两次异常才提醒；关闭提醒后冷却 ") + value.CooldownMinutes.ToString("0.#") + UiText.T(" 分钟。");
                bool startupSaved = true;
                try
                {
                    if (autoStart.Checked != StartupRegistration.Enabled) StartupRegistration.Set(autoStart.Checked, Application.ExecutablePath);
                }
                catch (Exception ex)
                {
                    feedback.ForeColor = Palette.Red;
                    feedback.Text = UiText.T("监测设置已保存，但开机启动设置失败：") + ex.Message;
                    startupSaved = false;
                    autoStart.Checked = StartupRegistration.Enabled;
                }
                if (startupSaved) settingsDialog.Hide();
                else settingsFeedback.Text = feedback.Text;
                await CheckNow();
            }
            catch (Exception ex) { settingsFeedback.ForeColor = Palette.Red; settingsFeedback.Text = UiText.T("设置保存失败：") + UiText.T(ex.Message); }
        }

        private void DetectProxy(object sender, EventArgs e)
        {
            try
            {
                var endpoint = NetworkProbe.DetectProxy();
                if (endpoint == null) { settingsFeedback.Text = UiText.T("未发现代理端口。请先启动 VPN，或手动输入端口。"); return; }
                port.Value = endpoint.Port;
                settingsFeedback.Text = UiText.T("识别到 " + endpoint.ProcessName + " · 127.0.0.1:" + endpoint.Port + "，保存后生效。");
            }
            catch (Exception ex) { settingsFeedback.Text = UiText.T("识别失败：") + ex.Message; }
        }

        private void TogglePause()
        {
            paused = !paused; policy.ResetSamples(); nextCheck = 0;
            pauseButton.Text = pauseMenu.Text = UiText.T(paused ? "继续监测" : "暂停监测");
            UpdateFooter();
        }
        private void HideToTray() { Hide(); ShowInTaskbar = false; }
        private void ShowMain() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void ExitApp()
        {
            if (exiting) return;
            PrepareExit();
            Application.Exit();
        }
        private void PrepareExit()
        {
            if (exiting) return;
            exiting = true; timer.Stop();
            if (alert != null) alert.Close();
            if (settingsDialog != null) settingsDialog.Close();
            tray.Visible = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); }
            else PrepareExit();
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Application.ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true;
                timer.Dispose(); tray.Dispose(); appIcon.Dispose();
                appearanceTips.Dispose();
                if (settingsDialog != null) settingsDialog.Dispose();
                if (exitWait != null) exitWait.Unregister(null);
                if (showWait != null) showWait.Unregister(null);
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= NetworkChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
                SystemEvents.PowerModeChanged -= PowerChanged;
            }
            base.Dispose(disposing);
        }
    }

    // Active only while an alert is shown. Mouse events continue to their original target.
    internal sealed class ScreenClickMonitor : IDisposable
    {
        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int kind, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly Control owner;
        private readonly Action clicked;
        private readonly Func<Point, bool> ignoreClick;
        private readonly MouseHook callback;
        private IntPtr hook;
        private bool pending;
        private bool disposed;

        public ScreenClickMonitor(Control owner, Action clicked)
            : this(owner, clicked, null) { }
        public ScreenClickMonitor(Control owner, Action clicked, Func<Point, bool> ignoreClick)
        {
            this.owner = owner; this.clicked = clicked; this.ignoreClick = ignoreClick; callback = OnMouse;
        }
        public bool Start()
        {
            if (disposed) return false;
            if (hook != IntPtr.Zero) return true;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            return hook != IntPtr.Zero;
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0)
                {
                    Point? position = ignoreClick != null && data != IntPtr.Zero ?
                        (Point?)new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)) : null;
                    HandleMouseMessage(message.ToInt32(), position);
                }
            }
            catch (Exception) { /* Never let managed exceptions escape a native hook. */ }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        internal void ProcessMouseMessage(int message)
        { HandleMouseMessage(message, null); }
        internal void HandleMouseMessage(int message, Point? position)
        {
            if (disposed || pending || owner.IsDisposed || !owner.IsHandleCreated) return;
            if (message != 0x0201 && message != 0x0204 && message != 0x0207 && message != 0x020B) return;
            if (position.HasValue && ignoreClick != null && ignoreClick(position.Value)) return;
            pending = true;
            try
            {
                owner.BeginInvoke(new Action(() => { if (!disposed && !owner.IsDisposed) clicked(); }));
            }
            catch (InvalidOperationException) { pending = false; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            IntPtr current = hook; hook = IntPtr.Zero;
            if (current != IntPtr.Zero) UnhookWindowsHookEx(current);
        }
    }

    internal sealed class AlertWindow : Form
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);
        private ScreenClickMonitor screenClick;
        private readonly Label note;
        private readonly Button exitButton;
        private bool dismissOnly;
        public bool ExitRequested { get; private set; }
        public AlertWindow(Icon icon, string reason, decimal minutes, bool test)
        {
            Text = "网络已掉线"; Tag = "网络已掉线"; Icon = icon; TopMost = true;
            Font = Palette.Font(10); BackColor = Palette.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(452, 236);
            var title = Palette.Label("网络已掉线", 23, Palette.Red); title.Dock = DockStyle.None; title.SetBounds(26, 22, 400, 46);
            var detail = Palette.Label(reason, 10); detail.Dock = DockStyle.None; detail.SetBounds(28, 76, 395, 52);
            note = Palette.Label("点击屏幕其他位置关闭提醒，冷却 " + minutes.ToString("0.#") + " 分钟。", 9, Palette.Muted);
            note.Dock = DockStyle.None; note.SetBounds(28, 134, 395, 28);
            exitButton = Palette.Button("手动关闭，退出检测", true); exitButton.Width = 205; exitButton.Location = new Point(219, 181);
            exitButton.Click += (s, e) => { ExitRequested = true; Close(); };
            AcceptButton = exitButton; CancelButton = exitButton;
            Controls.AddRange(new Control[] { title, detail, note, exitButton });
            DismissOnClick(this);
            UiText.Apply(this); Palette.Apply(this);
            using (var graphics = CreateGraphics())
            {
                float scale = graphics.DpiX / 96f;
                if (scale != 1f) Scale(new SizeF(scale, scale));
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            screenClick = new ScreenClickMonitor(this, Dismiss, IsExitTarget);
            if (!screenClick.Start())
            {
                note.Tag = "全屏点击不可用，可点文字暂时关闭。";
                note.Text = UiText.T((string)note.Tag);
            }
        }
        public void Dismiss()
        {
            dismissOnly = true;
            Close();
        }
        internal bool IsExitTarget(Point screenPoint)
        {
            if (exitButton.RectangleToScreen(exitButton.ClientRectangle).Contains(screenPoint)) return true;
            if (!Bounds.Contains(screenPoint)) return false;
            long packed = ((long)(screenPoint.Y & 0xffff) << 16) | (uint)(screenPoint.X & 0xffff);
            return SendMessage(Handle, 0x0084, IntPtr.Zero, new IntPtr(packed)).ToInt64() == 20; // HTCLOSE
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!dismissOnly && e.CloseReason == CloseReason.UserClosing) ExitRequested = true;
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (screenClick != null) screenClick.Dispose();
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && screenClick != null) screenClick.Dispose();
            base.Dispose(disposing);
        }

        private void DismissOnClick(Control control)
        {
            if (control == exitButton) return;
            control.Click += (s, e) => Dismiss();
            control.Cursor = Cursors.Hand;
            foreach (Control child in control.Controls) DismissOnClick(child);
        }
    }
}
