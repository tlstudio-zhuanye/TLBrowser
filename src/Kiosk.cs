using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TLBrowser;

/// <summary>
/// 企业模式（单页锁定 / kiosk）：启用之后浏览器只能待在指定页面，
/// 想去别的地方一律拦下，改设置或退出企业模式都要管理员密码。
///
/// 密码**不落明文**：只存 PBKDF2(SHA256) 的 salt + hash，
/// 就算有人翻到 kiosk.json 也拿不回密码。
///
/// 边界（要如实告诉使用者）：这份配置是本程序自己的文件，
/// 能直接删文件的人等于能解锁。它防的是「坐在屏幕前随手乱点」，
/// 不是防拿到本机管理员权限的人 —— 那是组策略/域控该管的事。
/// </summary>
internal static class Kiosk
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 120_000;

    private static readonly object Gate = new();
    private static Config _cfg = Load();

    public static string SettingsPath => Path.Combine(Brand.AppDataDir, "kiosk.json");

    /// <summary>企业模式是否已启用。</summary>
    public static bool Enabled
    {
        get { lock (Gate) return _cfg.Enabled; }
    }

    /// <summary>锁定的页面地址；未启用时返回空串。</summary>
    public static string Url
    {
        get { lock (Gate) return _cfg.Enabled ? _cfg.Url : ""; }
    }

    /// <summary>有没有设过管理员密码。</summary>
    public static bool HasPassword
    {
        get { lock (Gate) return _cfg.Salt.Length > 0 && _cfg.Hash.Length > 0; }
    }

    /// <summary>企业模式下，连同一台主机的子路径也放行（默认开）。</summary>
    public static bool AllowSameHost
    {
        get { lock (Gate) return _cfg.AllowSameHost; }
    }

    // ────────────────────────────── 判定 ──────────────────────────────

    /// <summary>
    /// 这个地址能不能去。企业模式没开就一律放行。
    /// 开了之后：锁定页本身永远放行；同主机默认也放行（否则页面里的
    /// 登录跳转、同域子资源都会被拦，绝大多数站点根本没法用）；
    /// 其余全部拒绝。
    /// </summary>
    public static bool Allows(string? url)
    {
        if (!Enabled) return true;
        return AllowsWith(url, Url, AllowSameHost);
    }

    /// <summary>
    /// 纯函数版判定：不读全局配置、不写盘，给自测用。
    /// 域名一律按分段比较（IsUnder），绝不用 Contains ——
    /// 否则 eviltlstudio.cn 就能混过 tlstudio.cn。
    /// </summary>
    public static bool AllowsWith(string? url, string? target, bool allowSameHost)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (string.IsNullOrWhiteSpace(target)) return false;

        // 浏览器内部页（新标签页 / about:blank）不属于「跑出去」
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith(HomePage.Origin, StringComparison.OrdinalIgnoreCase)) return true;

        if (!TryHost(url, out var host) || !TryHost(target, out var locked)) return false;
        if (host.Length == 0 || locked.Length == 0) return false;

        if (string.Equals(host, locked, StringComparison.OrdinalIgnoreCase)) return true;

        return allowSameHost && (IsUnder(host, locked) || IsUnder(locked, host));
    }

    private static bool IsUnder(string host, string root) =>
        host == root || host.EndsWith("." + root, StringComparison.Ordinal);

    private static bool TryHost(string raw, out string host)
    {
        host = "";
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        host = uri.Host.ToLowerInvariant();
        return host.Length > 0;
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
    /// 开启企业模式。密码不能为空 —— 没有密码等于锁不上。
    /// 网址会做一次规范化（没写 scheme 就补 https）。
    /// </summary>
    public static void Enable(string url, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("password required", nameof(password));

        var normalized = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("url required", nameof(url));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        lock (Gate)
        {
            _cfg = new Config
            {
                Enabled = true,
                Url = normalized,
                Salt = salt,
                Hash = Derive(password, salt, Iterations),
                Iterations = Iterations,
                AllowSameHost = _cfg.AllowSameHost,
                UpdatedAt = DateTime.Now
            };
            SaveLocked();
        }
    }

    /// <summary>改锁定页（调用前必须先 Verify 通过）。</summary>
    public static void SetUrl(string url)
    {
        var normalized = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("url required", nameof(url));

        lock (Gate)
        {
            _cfg.Url = normalized;
            _cfg.UpdatedAt = DateTime.Now;
            SaveLocked();
        }
    }

    /// <summary>改「同主机放行」（调用前必须先 Verify 通过）。</summary>
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

    /// <summary>关闭企业模式（调用前必须先 Verify 通过）。密码保留，下次启用不用重设。</summary>
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

    private static string NormalizeUrl(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        if (!s.Contains("://")) s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) &&
               (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? u.ToString()
            : "";
    }

    // ────────────────────────────── 落盘 ──────────────────────────────

    private sealed class Config
    {
        public bool Enabled { get; set; }
        public string Url { get; set; } = "";
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
            var c = JsonSerializer.Deserialize<Config>(text);
            if (c is null) return new Config();
            try { c.Salt = Convert.FromBase64String(c.SaltB64); } catch { c.Salt = Array.Empty<byte>(); }
            try { c.Hash = Convert.FromBase64String(c.HashB64); } catch { c.Hash = Array.Empty<byte>(); }
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
