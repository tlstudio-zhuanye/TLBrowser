using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TLBrowser;

/// <summary>
/// 企业模式（网站白名单）：给员工用的。启用之后浏览器**只能访问名单里的网站**，
/// 名单之外（外面随便什么站）一律拦下，改名单或退出企业模式都要管理员密码。
///
/// 名单条目写成网址即可（`oa.tlstudio.cn`、`http://10.0.0.5:8080/` 都认），
/// 内部只按 **主机[:端口]** 比对，协议不参与 —— 内网系统常常 http / https 都在用。
/// 条目之间的顺序有意义：**第一条就是启动页**，开浏览器直接进它。
///
/// 密码**不落明文**：只存 PBKDF2(SHA256) 的 salt + hash，
/// 就算有人翻到 kiosk.json 也拿不回密码。
///
/// 边界（要如实告诉使用者）：这份配置是本程序自己的文件，
/// 能直接删文件的人等于能解锁。它防的是「员工在浏览器里乱逛」，
/// 不是防拿到本机管理员权限的人 —— 那是组策略/域控该管的事。
/// </summary>
internal static class Kiosk
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 120_000;
    private const int MaxSites = 200;

    private static readonly object Gate = new();
    private static Config _cfg = Load();

    public static string SettingsPath => Path.Combine(Brand.AppDataDir, "kiosk.json");

    /// <summary>企业模式是否已启用。</summary>
    public static bool Enabled
    {
        get { lock (Gate) return _cfg.Enabled; }
    }

    /// <summary>
    /// 已配置的网址名单（第一条是启动页）。**不管有没有启用都返回** ——
    /// 设置窗要在「未启用」状态下也能接着改上次存下的名单，不用重填。
    /// 真正生效与否看 <see cref="Enabled"/>。
    /// </summary>
    public static IReadOnlyList<string> Allowed
    {
        get { lock (Gate) return _cfg.Allowed.ToArray(); }
    }

    /// <summary>名单条数（未启用时 0）。</summary>
    public static int Count
    {
        get { lock (Gate) return _cfg.Enabled ? _cfg.Allowed.Count : 0; }
    }

    /// <summary>启动页 / 主页按钮去哪。名字单为空时返回空串，由调用方退回新标签页。</summary>
    public static string StartUrl
    {
        get
        {
            lock (Gate)
            {
                if (!_cfg.Enabled) return "";
                if (!string.IsNullOrWhiteSpace(_cfg.Start)) return _cfg.Start;
                foreach (var s in _cfg.Allowed)
                {
                    var u = NormalizeUrl(s);
                    if (u.Length > 0) return u;
                }
                return "";
            }
        }
    }

    /// <summary>有没有设过管理员密码。</summary>
    public static bool HasPassword
    {
        get { lock (Gate) return _cfg.Salt.Length > 0 && _cfg.Hash.Length > 0; }
    }

    /// <summary>企业模式下，名单里的域名连同它的子域一起放行（默认开）。</summary>
    public static bool AllowSameHost
    {
        get { lock (Gate) return _cfg.AllowSameHost; }
    }

    // ────────────────────────────── 判定 ──────────────────────────────

    /// <summary>这个地址能不能去。企业模式没开就一律放行；开了就只能去名单里的站。</summary>
    public static bool Allows(string? url)
    {
        if (!Enabled) return true;
        lock (Gate) return AllowsUrl(url, _cfg.Allowed, _cfg.AllowSameHost);
    }

    /// <summary>
    /// 判定本体：不读全局配置、不写盘，给自测用。
    /// 先看是不是浏览器自己的内部页（新标签页 / about:），是就放行；
    /// 再把地址和名单逐条比主机[:端口]。域名一律按分段比较（EndsWith "." + 根域），
    /// 绝不用 Contains —— 否则 eviltlstudio.cn 就能混过 tlstudio.cn。
    /// </summary>
    public static bool AllowsUrl(string? url, IReadOnlyList<string>? allowed, bool allowSub)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // 浏览器内部页（新标签页 / about:blank）不属于「跑出去」
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith(HomePage.Origin, StringComparison.OrdinalIgnoreCase)) return true;

        if (!TrySplit(url, out var host, out var port)) return false;
        if (allowed is null) return false;

        foreach (var raw in allowed)
        {
            if (!TrySplit(raw, out var ok, out var oport)) continue;

            if (string.Equals(host, ok, StringComparison.OrdinalIgnoreCase) &&
                (oport == 0 || oport == port))
                return true;

            // 子域放行：只在名单条目**没写端口**、且它本身是个域名（不是 IP）时才生效，
            // 而且方向是单向的 —— 填 tlstudio.cn 连带 oa.tlstudio.cn，
            // 填 oa.tlstudio.cn 不等于放行 tlstudio.cn 全站。
            if (allowSub && oport == 0 && IsDomainName(ok) &&
                host.EndsWith("." + ok, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>把地址拆成「主机 + 端口」，端口 0 表示没显式写（或就是默认端口）。</summary>
    private static bool TrySplit(string? raw, out string host, out int port)
    {
        host = "";
        port = 0;

        var s = (raw ?? "").Trim();
        if (s.Length == 0) return false;
        if (s.Contains("://"))
        {
            // 只认 http/https；别的协议（file: / javascript: 等）一律不参与白名单
        }
        else
        {
            s = "https://" + s;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        host = uri.Host.ToLowerInvariant();
        if (host.Length == 0) return false;
        port = uri.IsDefaultPort ? 0 : uri.Port;
        return true;
    }

    /// <summary>看着像域名（有字母），而不是纯 IP —— 纯 IP 不做子域匹配。</summary>
    private static bool IsDomainName(string host)
    {
        foreach (var ch in host)
            if (char.IsLetter(ch)) return true;
        return false;
    }

    // ────────────────────────────── 密码 ──────────────────────────────

    /// <summary>校验管理员密码。空密码永远不通过（避免没设密码时被人空着蒙进去）。</summary>
    public static bool Verify(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        if (!HasPassword) return false;

        byte[] salt, hash;
        int iterations;
        lock (Gate)
        {
            salt = _cfg.Salt;
            hash = _cfg.Hash;
            iterations = _cfg.Iterations > 0 ? _cfg.Iterations : Iterations;
        }

        try
        {
            var got = Derive(password, salt, iterations);
            // 定长时间比较，别让比较耗时泄露密码前缀
            return CryptographicOperations.FixedTimeEquals(got, hash);
        }
        catch { return false; }
    }

    /// <summary>
    /// 开启企业模式。密码不能为空，名单至少要有一条有效网址 ——
    /// 名单空着等于把浏览器锁死，那不如不开。
    /// </summary>
    public static void Enable(IEnumerable<string> sites, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("password required", nameof(password));

        var list = CleanList(sites);
        if (list.Count == 0)
            throw new ArgumentException("at least one site required", nameof(sites));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        lock (Gate)
        {
            _cfg = new Config
            {
                Enabled = true,
                Allowed = list,
                Start = "",
                Salt = salt,
                Hash = Derive(password, salt, Iterations),
                Iterations = Iterations,
                AllowSameHost = _cfg.AllowSameHost,
                UpdatedAt = DateTime.Now
            };
            SaveLocked();
        }
    }

    /// <summary>改网站名单（调用前必须先 Verify 通过）。顺序有意义：第一条是启动页。</summary>
    public static void SetAllowed(IEnumerable<string> sites)
    {
        var list = CleanList(sites);
        if (list.Count == 0)
            throw new ArgumentException("at least one site required", nameof(sites));

        lock (Gate)
        {
            _cfg.Allowed = list;
            _cfg.Start = "";
            _cfg.UpdatedAt = DateTime.Now;
            SaveLocked();
        }
    }

    /// <summary>改「同域名放行子域」（调用前必须先 Verify 通过）。</summary>
    public static void SetAllowSameHost(bool allow)
    {
        lock (Gate)
        {
            _cfg.AllowSameHost = allow;
            _cfg.UpdatedAt = DateTime.Now;
            SaveLocked();
        }
    }

    /// <summary>改管理员密码（调用前必须先 Verify 通过旧密码）。</summary>
    public static void SetPassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("password required", nameof(newPassword));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        lock (Gate)
        {
            _cfg.Salt = salt;
            _cfg.Hash = Derive(newPassword, salt, Iterations);
            _cfg.Iterations = Iterations;
            _cfg.UpdatedAt = DateTime.Now;
            SaveLocked();
        }
    }

    /// <summary>关闭企业模式（调用前必须先 Verify 通过）。名单和密码保留，下次启用不用重填。</summary>
    public static void Disable()
    {
        lock (Gate)
        {
            _cfg.Enabled = false;
            _cfg.UpdatedAt = DateTime.Now;
            SaveLocked();
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeyBytes);
    }

    /// <summary>把一个条目规范化成可导航的网址；认不出来返回空串（界面上用它判断「这条能不能加」）。</summary>
    public static string NormalizeUrl(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        if (!s.Contains("://")) s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) &&
               (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? u.ToString()
            : "";
    }

    /// <summary>去重 + 丢掉认不出来的，保持原顺序（第一条仍然是启动页）。</summary>
    private static List<string> CleanList(IEnumerable<string>? sites)
    {
        var list = new List<string>();
        if (sites is null) return list;

        foreach (var raw in sites)
        {
            var u = NormalizeUrl(raw);
            if (u.Length == 0) continue;
            if (list.Any(x => string.Equals(x, u, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(u);
            if (list.Count >= MaxSites) break;
        }
        return list;
    }

    // ────────────────────────────── 落盘 ──────────────────────────────

    private sealed class Config
    {
        public bool Enabled { get; set; }

        /// <summary>允许访问的网址。v1.1.6 起是名单；旧版本是单页锁定，见 <see cref="LegacyUrl"/>。</summary>
        [JsonPropertyName("allowed")]
        public List<string> Allowed { get; set; } = new();

        /// <summary>可选：指定启动页。留空就用名单第一条。</summary>
        [JsonPropertyName("start")]
        public string Start { get; set; } = "";

        /// <summary>v1.1.5 及以前的「锁定页面」字段，只用来做一次性迁移。</summary>
        [JsonPropertyName("Url")]
        public string LegacyUrl { get; set; } = "";

        public bool AllowSameHost { get; set; } = true;
        public int Iterations { get; set; } = 120_000;

        [JsonPropertyName("salt")]
        public string SaltB64 { get; set; } = "";

        [JsonPropertyName("hash")]
        public string HashB64 { get; set; } = "";

        [JsonIgnore] public byte[] Salt { get; set; } = Array.Empty<byte>();
        [JsonIgnore] public byte[] Hash { get; set; } = Array.Empty<byte>();

        public DateTime UpdatedAt { get; set; }
    }

    private static Config Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new Config();
            var text = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var c = JsonSerializer.Deserialize<Config>(text) ?? new Config();
            try { c.Salt = Convert.FromBase64String(c.SaltB64); } catch { c.Salt = Array.Empty<byte>(); }
            try { c.Hash = Convert.FromBase64String(c.HashB64); } catch { c.Hash = Array.Empty<byte>(); }

            c.Allowed ??= new List<string>();
            c.Start ??= "";

            // 旧版单页锁定 → 迁移成一行名单，免得升级完把人的配置丢了
            if (c.Allowed.Count == 0 && !string.IsNullOrWhiteSpace(c.LegacyUrl))
            {
                var u = NormalizeUrl(c.LegacyUrl);
                if (u.Length > 0) c.Allowed.Add(u);
            }

            return c;
        }
        catch { return new Config(); }
    }

    private static void SaveLocked()
    {
        try
        {
            _cfg.SaltB64 = Convert.ToBase64String(_cfg.Salt);
            _cfg.HashB64 = Convert.ToBase64String(_cfg.Hash);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { }
    }
}
