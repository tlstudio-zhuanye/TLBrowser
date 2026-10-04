using System.Text;
using System.Text.Json;

namespace TLBrowser;

/// <summary>
/// 本浏览器的安全与拦截中心：广告/跟踪拦截、权限拒绝记录、弹窗拦截记录、外部协议拦截记录。
///
/// 设计上的三条硬规矩：
///  1. **主文档永不拦**。拦截只作用在子资源上（图片/脚本/样式/XHR/媒体/字体）。
///     这样最坏结果是页面少个组件，不会出现「点了链接一片白」——那是 360 被骂的原因之一。
///  2. **匹配按域名分段**，不用 Contains。`eviltlstudio.cn` 不能命中 `tlstudio.cn`，
///     `notdoubleclick.net` 也不能命中 `doubleclick.net`。
///  3. **两个官网永远放行**，任何情况下都不拦。
/// </summary>
internal static class Safety
{
    private sealed class Settings
    {
        public bool AdBlock { get; set; } = true;
        public bool DenyPermissions { get; set; } = true;
        public bool BlockAutoPopups { get; set; } = true;
        public bool AskExternalScheme { get; set; } = true;
        public bool ConfirmDownload { get; set; } = true;
    }

    private static readonly object Gate = new();
    private static readonly HashSet<string> BlockHosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BlockLabels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllowHosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> BlockedLog = new();
    private static readonly List<string> DeniedLog = new();
    private static Settings _settings = new();
    private static int _blockedTotal;
    private static int _popupBlocked;
    private static int _externalAsked;
    private static int _rulesLoaded;

    public const string ResAdHosts = "TLBrowser.assets.adhosts.txt";

    public static string SettingsPath => Path.Combine(Brand.AppDataDir, "settings.json");
    public static string UserRulesPath => Path.Combine(Brand.AppDataDir, "adblock_user.txt");
    public static string AuditLogPath => Path.Combine(Brand.AppDataDir, "blocked.log");

    /// <summary>
    /// 拦截审计日志。状态条上只有一个数字，用户想知道「到底拦了什么」得能翻出来。
    /// 超过 512KB 就截掉前半段——只留最近的，免得长期用下来变成几十兆。
    /// </summary>
    private static void Audit(string kind, string detail)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\t{detail}{Environment.NewLine}";
            File.AppendAllText(AuditLogPath, line, new UTF8Encoding(false));

            var info = new FileInfo(AuditLogPath);
            if (info.Length > 512 * 1024)
            {
                var lines = File.ReadAllLines(AuditLogPath, Encoding.UTF8);
                var keep = lines.Skip(lines.Length / 2);
                File.WriteAllLines(AuditLogPath, keep, new UTF8Encoding(false));
            }
        }
        catch { }
    }

    // ────────────────────────────── 开关 ──────────────────────────────

    public static bool AdBlock
    {
        get => _settings.AdBlock;
        set { _settings.AdBlock = value; SaveSettings(); }
    }

    public static bool DenyPermissions
    {
        get => _settings.DenyPermissions;
        set { _settings.DenyPermissions = value; SaveSettings(); }
    }

    public static bool BlockAutoPopups
    {
        get => _settings.BlockAutoPopups;
        set { _settings.BlockAutoPopups = value; SaveSettings(); }
    }

    public static bool AskExternalScheme
    {
        get => _settings.AskExternalScheme;
        set { _settings.AskExternalScheme = value; SaveSettings(); }
    }

    public static bool ConfirmDownload
    {
        get => _settings.ConfirmDownload;
        set { _settings.ConfirmDownload = value; SaveSettings(); }
    }

    // ────────────────────────────── 统计 ──────────────────────────────

    public static int BlockedTotal { get { lock (Gate) return _blockedTotal; } }
    public static int PopupBlocked { get { lock (Gate) return _popupBlocked; } }
    public static int ExternalAsked { get { lock (Gate) return _externalAsked; } }
    public static int DeniedTotal { get { lock (Gate) return DeniedLog.Count; } }
    public static int RuleCount => _rulesLoaded;

    public static List<string> RecentBlocked(int max)
    {
        lock (Gate) return BlockedLog.Take(max).ToList();
    }

    public static List<string> RecentDenied(int max)
    {
        lock (Gate) return DeniedLog.Take(max).ToList();
    }

    /// <summary>守护面板打开时清零「未读」计数用。</summary>
    public static void ResetCounters()
    {
        lock (Gate)
        {
            _blockedTotal = 0;
            _popupBlocked = 0;
            _externalAsked = 0;
            BlockedLog.Clear();
            DeniedLog.Clear();
        }
    }

    // ────────────────────────────── 规则 ──────────────────────────────

    public static void Load()
    {
        LoadSettings();
        LoadRules();
    }

    private static void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                // 第一次运行就把默认值写出来。不然用户根本不知道有这些开关可以关，
                // 遇到「站点被误拦」只能干瞪眼。
                SaveSettings();
                return;
            }

            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
            if (s is not null) _settings = s;
        }
        catch { _settings = new Settings(); }
    }

    private static void SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void LoadRules()
    {
        lock (Gate)
        {
            BlockHosts.Clear();
            BlockLabels.Clear();
            AllowHosts.Clear();

            Parse(ReadEmbedded(ResAdHosts));
            EnsureUserRulesFile();
            Parse(ReadFile(UserRulesPath));

            _rulesLoaded = BlockHosts.Count + BlockLabels.Count;
        }
    }

    private static void Parse(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('+'))
            {
                var host = Normalize(line[1..]);
                if (host.Length > 0) AllowHosts.Add(host);
                continue;
            }

            if (line.StartsWith('='))
            {
                var label = line[1..].Trim().ToLowerInvariant();
                if (label.Length > 0) BlockLabels.Add(label);
                continue;
            }

            // 用户文件里允许 `||example.com^` 这种从别处抄来的写法，去掉装饰符
            line = line.TrimStart('|', '.').TrimEnd('^', '|', '.').Trim();
            var h = Normalize(line);
            if (h.Length > 0) BlockHosts.Add(h);
        }
    }

    private static string Normalize(string s)
    {
        s = s.Trim().TrimEnd('.').ToLowerInvariant();
        if (s.StartsWith("http://")) s = s[7..];
        else if (s.StartsWith("https://")) s = s[8..];
        var slash = s.IndexOf('/');
        if (slash >= 0) s = s[..slash];
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        return s;
    }

    private static string ReadEmbedded(string name)
    {
        try
        {
            using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s is null) return "";
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch { return ""; }
    }

    private static string ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }
        catch { return ""; }
    }

    /// <summary>第一次运行时放一个带说明的模板，让用户知道可以自己加规则。</summary>
    private static void EnsureUserRulesFile()
    {
        try
        {
            if (File.Exists(UserRulesPath)) return;
            File.WriteAllText(UserRulesPath, """
# TL 浏览器 · 自定义拦截规则
#
# 一行一条：
#   example.com    拦截这个域（含子域）
#   =ad            拦截「host 里有一整段正好叫 ad」的域名
#   +example.com   放行（站点被误拦时写这条）
#
# 改完重启本程序生效。删掉整个文件就回到只有内置规则的状态。

""", new UTF8Encoding(false));
        }
        catch { }
    }

    // ────────────────────────────── 判定 ──────────────────────────────

    /// <summary>两个官网永远放行。放在最前面，后面任何规则都改不了它。</summary>
    private static bool IsBrandHost(string host) =>
        Brand.IsBrandHostName(host);

    /// <summary>不看开关，纯判规则。自检和调试用——否则关掉拦截就测不出规则对不对。</summary>
    public static bool Evaluate(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        var host = parsed.Host.ToLowerInvariant();
        if (host.Length == 0) return false;
        if (IsBrandHost(host)) return false;

        lock (Gate)
        {
            foreach (var allow in AllowHosts)
                if (IsUnder(host, allow)) return false;

            foreach (var rule in BlockHosts)
                if (IsUnder(host, rule)) return true;

            if (BlockLabels.Count > 0)
                foreach (var label in host.Split('.'))
                    if (BlockLabels.Contains(label)) return true;

            return false;
        }
    }

    public static bool ShouldBlock(string? uri) => _settings.AdBlock && Evaluate(uri);

    private static bool IsUnder(string host, string root) =>
        host == root || host.EndsWith("." + root, StringComparison.Ordinal);

    public static void CountBlocked(string uri)
    {
        var host = HostOf(uri);
        lock (Gate)
        {
            _blockedTotal++;
            BlockedLog.Insert(0, host);
            while (BlockedLog.Count > 200) BlockedLog.RemoveAt(BlockedLog.Count - 1);
        }
        Audit("广告", host);
    }

    public static void CountPopupBlocked(string uri)
    {
        var host = HostOf(uri);
        lock (Gate)
        {
            _popupBlocked++;
            BlockedLog.Insert(0, "弹窗 · " + host);
            while (BlockedLog.Count > 200) BlockedLog.RemoveAt(BlockedLog.Count - 1);
        }
        Audit("弹窗", host);
    }

    public static void CountExternalAsked(string uri)
    {
        lock (Gate)
        {
            _externalAsked++;
            BlockedLog.Insert(0, "外部协议 · " + Shorten(uri));
            while (BlockedLog.Count > 200) BlockedLog.RemoveAt(BlockedLog.Count - 1);
        }
        Audit("外部协议", Shorten(uri));
    }

    public static void CountPermissionDenied(string kind, string host)
    {
        lock (Gate)
        {
            DeniedLog.Insert(0, $"{kind} · {host}");
            while (DeniedLog.Count > 100) DeniedLog.RemoveAt(DeniedLog.Count - 1);
        }
        Audit("权限", kind + " · " + host);
    }

    public static string HostOf(string uri)
    {
        try { return new Uri(uri).Host; } catch { return Shorten(uri); }
    }

    private static string Shorten(string s) => s.Length > 80 ? s[..77] + "..." : s;
}
