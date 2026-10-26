using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NetWatch
{
    internal static class UiText
    {
        public static string Language = "zh";
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "网络状态", "Network Status" }, { "网络", "Network" }, { "设置", "Settings" },
            { "本机 IP", "Local IP" }, { "正在检查", "Checking" }, { "正在读取…", "Reading…" },
            { "读取 Windows 网络状态…", "Reading network status..." }, { "读取本地代理服务…", "Reading local proxy..." },
            { "本机局域网地址", "Local LAN address" }, { "准备开始监测", "Starting monitor" },
            { "已连接", "Connected" }, { "网络已断开", "Disconnected" }, { "已断开", "Disconnected" },
            { "本地服务正常", "Service running" }, { "未启用", "Disabled" }, { "网卡已连接", "Adapter connected" },
            { "状态未知", "Unknown" }, { "未分配", "Not assigned" }, { "读取失败", "Read failed" },
            { "Windows 报告已连接 Internet", "Windows reports Internet access" },
            { "Windows 报告无 Internet 连接", "Windows reports no Internet access" },
            { "VPN 监测已关闭", "VPN monitoring disabled" },
            { "监测 VPN", "Monitor VPN" }, { "检测间隔 / 秒", "Check / seconds" }, { "冷却 / 分钟", "Cooldown / min" },
            { "代理端口", "Proxy port" }, { "VPN 网卡", "VPN adapter" }, { "自动识别", "Detect" },
            { "本地代理（Clash / v2rayN 等）", "Local proxy (Clash / v2rayN)" }, { "指定 VPN 网卡", "Selected VPN adapter" },
            { "开机登录后，等待联网再静默监测", "Start at sign-in; wait for network" },
            { "仅检测本机状态；IP 为局域网地址。\n本地代理正常不代表远端节点可用。", "Local checks only; IP is a LAN address.\nA running proxy does not verify its remote server." },
            { "保存", "Save" }, { "取消", "Cancel" }, { "测试提醒", "Test alert" },
            { "手动关闭，退出检测", "Stop and exit" },
            { "暂停监测", "Pause" }, { "继续监测", "Resume" }, { "打开主界面", "Open" }, { "退出", "Exit" },
            { "关闭窗口隐藏到托盘 · 托盘右键可退出", "Close to tray; right-click tray to exit" },
            { "设置已保存。连续两次异常才提醒；关闭提醒后冷却 ", "Saved. Alert after 2 failed checks; cooldown: " },
            { "已暂停", "Paused" }, { "监测中", "Monitoring" }, { "等待联网后开始监测", "Waiting for network" },
            { "联网 / 唤醒准备中", "Waiting for network to settle" }, { "最近检测", "Checked" },
            { "冷却：关闭弹窗后开始", "Cooldown starts when alert closes" }, { "冷却：未开始", "No cooldown" },
            { "网络断开", "Network disconnected" }, { "VPN 断开", "VPN disconnected" }, { "连接正常", "Connected" },
            { "网络已掉线", "Network disconnected" },
            { "Windows 报告网络未连接到 Internet", "Windows reports no Internet connection" },
            { "VPN 本机服务或目标网卡已断开", "VPN local service or adapter disconnected" },
            { "这是一条测试提醒，未实际断开网络。", "This is a test. Your network was not disconnected." },
            { "全屏点击不可用，可点文字暂时关闭。", "Click popup text to snooze." },
            { "未发现代理端口。请先启动 VPN，或手动输入端口。", "No proxy found. Start your VPN or enter its port." },
            { "检测间隔需为 5–300 秒。", "Check interval must be 5–300 seconds." },
            { "冷却时间需为 0.1–1440 分钟。", "Cooldown must be 0.1–1440 minutes." },
            { "代理端口需为 1–65535。", "Proxy port must be 1–65535." },
            { "VPN 检测方式无效。", "Invalid VPN detection mode." },
            { "请输入目标 VPN 网卡的完整名称。", "Enter the full VPN adapter name." },
            { "切换黑白配色", "Switch light / dark theme" }, { "切换中英文", "Switch Chinese / English" }
        };
        private static readonly string[,] Fragments =
        {
            { "监测设置已保存，但开机启动设置失败：", "Settings saved, but startup setup failed: " },
            { "设置未能读取，暂用默认值：", "Could not load settings; using defaults: " },
            { "设置已保存。连续两次异常才提醒；关闭提醒后冷却 ", "Saved. Alert after 2 failed checks; cooldown: " },
            { "无法读取 Windows 网络状态：", "Could not read Windows network status: " },
            { "无法读取 VPN 状态：", "Could not read VPN status: " },
            { "检测暂时失败：", "Check failed: " }, { "设置保存失败：", "Could not save settings: " },
            { "外观设置保存失败：", "Could not save appearance: " }, { "识别失败：", "Detection failed: " },
            { "未找到网卡：", "Adapter not found: " }, { "识别到 ", "Detected " }, { "，保存后生效。", "; save to apply." },
            { "（不是已知代理进程）", " (not a known proxy process)" }, { " 正在监听", " listening" },
            { " · 未监听", " · not listening" }, { " · 已连接", " · connected" }, { " · 已断开", " · disconnected" },
            { " · 局域网地址", " · LAN address" }, { "冷却剩余 ", "Cooldown " },
            { "点击任意位置关闭，冷却 ", "Click anywhere · Snooze: " },
            { "点击屏幕任意位置关闭，冷却 ", "Click anywhere on screen · Snooze: " },
            { "点击屏幕其他位置关闭提醒，冷却 ", "Click elsewhere · Snooze: " },
            { "关闭后 ", "No more alerts for " }, { " 分钟内不再提醒。", " minutes after closing." },
            { " 分钟。", " min." }, { "；", "; " }
        };

        public static string T(string text)
        {
            if (Language != "en" || string.IsNullOrEmpty(text)) return text;
            string value;
            if (English.TryGetValue(text, out value)) return value;
            // Combined fault reasons are translated separately before joining.
            if (text.Contains("；"))
            {
                string[] parts = text.Split(new[] { "；" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++) parts[i] = T(parts[i]);
                return string.Join("; ", parts);
            }
            for (int i = 0; i < Fragments.GetLength(0); i++) text = text.Replace(Fragments[i, 0], Fragments[i, 1]);
            return text;
        }

        public static void Apply(Control root)
        {
            if ((root is CheckBox || root is Form) && root.Tag == null) root.Tag = root.Text;
            var source = root.Tag as string;
            if (source != null && (root is Label || root is Button || root is CheckBox || root is Form)) root.Text = T(source);
            foreach (Control child in root.Controls) Apply(child);
        }
    }
}
