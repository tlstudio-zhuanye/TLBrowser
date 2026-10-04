using System.Drawing;
using System.Reflection;
using System.Text;

namespace TLBrowser;

internal static class Brand
{
    public const string AppName = "TL 浏览器";
    public const string AppVersion = "1.1.0";
    public const string Company = "TLSTUDIO";

    public const string SiteTl = "https://tlstudio.cn";
    public const string SiteDoubler = "https://tldoublerstudio.cn";
    public const string SearchTemplate = "https://www.bing.com/search?q=";
    /// <summary>首页表单里写死的那条搜索地址（不带 ?q=），主页守护拿它做比对。</summary>
    public const string SearchHostUrl = "https://www.bing.com/search";

    public const string DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>更新清单地址：Cloudflare Worker 上的 version.json（挂在 api.tlstudio.cn，
    /// 国内快、CDN 缓存），打不开再退 raw.githubusercontent。清单里写最新版本号、exe 直链与更新说明。</summary>
    public const string UpdateManifestUrl =
        "https://api.tlstudio.cn/tlbrowser/version.json";
    public const string UpdateManifestUrlAlt =
        "https://raw.githubusercontent.com/tlstudio-zhuanye/TLBrowser/main/version.json";

    /// <summary>点「打开下载页」跳的官网下载页（tlstudio.cn 上的浏览器页面）。</summary>
    public const string UpdatePageUrl =
        "https://tlstudio.cn/llq.html";

    public static readonly Color Blue = Color.FromArgb(0x1D, 0x9C, 0xD8);
    public static readonly Color BlueDark = Color.FromArgb(0x14, 0x7F, 0xB4);
    public static readonly Color Cyan = Color.FromArgb(0x38, 0xE1, 0xFF);
    public static readonly Color Purple = Color.FromArgb(0x7C, 0x5C, 0xFF);

    public static readonly Color ChromeBg = Color.FromArgb(0xF4, 0xF6, 0xF9);
    public static readonly Color ChromeLine = Color.FromArgb(0xE1, 0xE6, 0xEC);
    public static readonly Color TabActiveBg = Color.White;
    public static readonly Color TabHoverBg = Color.FromArgb(0xE7, 0xEC, 0xF2);
    public static readonly Color TextMain = Color.FromArgb(0x1F, 0x24, 0x2B);
    public static readonly Color TextDim = Color.FromArgb(0x6B, 0x74, 0x80);
    public static readonly Color StatusBg = Color.FromArgb(0xF0, 0xF3, 0xF7);

    /// <summary>本程序自己的数据目录（缓存 / 首页 / 守护日志都在这下面）。</summary>
    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TLSTUDIO", "TLBrowser");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>当前运行的可执行文件路径（快捷方式自愈时要拿它比对）。</summary>
    public static string AppExePath => Environment.ProcessPath ?? "";

    public static string GuardLogPath => Path.Combine(AppDataDir, "guard.log");

    /// <summary>WebView2 用户数据（缓存 / Cookie / localStorage）都落在这里，不污染别的目录。</summary>
    public static string UserDataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TLSTUDIO", "TLBrowser", "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>品牌首页（新标签页）的落地目录。</summary>
    public static string HomeDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TLSTUDIO", "TLBrowser", "home");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string HomeFilePath => Path.Combine(HomeDir, "index.html");

    public static string HomeUrl => new Uri(HomeFilePath).AbsoluteUri;

    public const string ResHome = "TLBrowser.assets.home.html";
    public const string ResLogo = "TLBrowser.assets.logo.png";
    public const string ResLogoDoubler = "TLBrowser.assets.logo_doubler.png";

    /// <summary>把内嵌的图片直接读成 Bitmap（脱离流生命周期，可安全长期持有）。</summary>
    public static Image? LoadImage(string resourceName)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (s is null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var raw = Image.FromStream(ms);
            return new Bitmap(raw);
        }
        catch { return null; }
    }

    /// <summary>把内嵌的文本资源读出来，并替换掉里面的占位符。</summary>
    public static string ReadTextResource(string resourceName)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (s is null) return "";
            using var reader = new StreamReader(s, Encoding.UTF8);
            return reader.ReadToEnd()
                .Replace("%%VERSION%%", AppVersion)
                .Replace("%%SITE_TL%%", SiteTl)
                .Replace("%%SITE_DOUBLER%%", SiteDoubler);
        }
        catch { return ""; }
    }

    /// <summary>
    /// 首页写盘之后「应该长什么样」。主页守护拿它和磁盘上的文件做逐字节比对，
    /// 只要被任何程序改过一个字符就会被发现并还原。
    /// </summary>
    public static string ExpectedHomeHtml => ReadTextResource(ResHome);

    /// <summary>把内嵌的首页与 logo 释放到磁盘（每次启动覆盖，保证和程序同版本）。</summary>
    public static void MaterializeHome()
    {
        var html = ExpectedHomeHtml;
        if (html.Length > 0)
            File.WriteAllText(HomeFilePath, html, new UTF8Encoding(false));
        WriteBinaryResource(ResLogo, Path.Combine(HomeDir, "logo.png"));
    }

    private static void WriteBinaryResource(string resourceName, string target)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (s is null) return;
        using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        s.CopyTo(fs);
    }

    /// <summary>
    /// 只认这两个官网（含子域）。主页守护用它当启动参数白名单：
    /// 外部程序习惯往快捷方式里塞一个网址来改启动页，白名单之外一律丢掉。
    /// </summary>
    public static bool IsBrandUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host.Length == 0) return false;
        return IsUnder(host, "tlstudio.cn") || IsUnder(host, "tldoublerstudio.cn");
    }

    private static bool IsUnder(string host, string root) =>
        host == root || host.EndsWith("." + root, StringComparison.Ordinal);

    /// <summary>
    /// 光凭域名判断是不是自家站。地址栏联想的放行、广告拦截的豁免都用它，
    /// 所以单独开一个口子，避免各处重复写一遍 IsUnder。
    /// </summary>
    public static bool IsBrandHostName(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().ToLowerInvariant();
        return IsUnder(h, "tlstudio.cn") || IsUnder(h, "tldoublerstudio.cn");
    }

    /// <summary>把用户输入拼成可导航的 URL；不是网址就走必应搜索。</summary>
    public static string NormalizeInput(string raw)
    {
        var q = raw.Trim();
        if (q.Length == 0) return HomeUrl;

        if (q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("edge://", StringComparison.OrdinalIgnoreCase))
            return q;

        if (q.StartsWith("//")) return "https:" + q;

        if (LooksLikeHost(q)) return "https://" + q;

        return SearchTemplate + Uri.EscapeDataString(q);
    }

    private static bool LooksLikeHost(string s)
    {
        if (s.Contains(' ')) return false;
        var host = s.Split('/')[0];
        if (!host.Contains('.')) return false;
        if (host.StartsWith('.') || host.EndsWith('.')) return false;
        foreach (var ch in host)
        {
            var ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                     (ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '_' || ch == ':';
            if (!ok) return false;
        }
        // 至少得有个字母组成的顶级域，避免把 "3.14" 之类当网址
        var parts = host.Split(':')[0].Split('.');
        if (parts.Length < 2) return false;
        var tld = parts[^1];
        if (tld.Length < 2) return false;
        foreach (var ch in tld)
            if (!char.IsLetter(ch)) return false;
        return true;
    }

    /// <summary>两个官网的裸域名。地址栏联想与 Tab 补全都按这个表来。</summary>
    public static readonly string[] BrandHosts = { "tlstudio.cn", "tldoublerstudio.cn" };

    /// <summary>地址栏联想里判断「这一条输入像不像地址」，决定要不要给出「直接访问」那一条。</summary>
    public static bool IsAddressLike(string raw)
    {
        var q = raw.Trim();
        if (q.Length == 0 || q.Contains(' ')) return false;
        if (q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        return LooksLikeHost(q);
    }

    /// <summary>
    /// Tab 补全：输入是两个官网域名的前缀时补全它。
    /// 只补域名，不做「猜你想去哪个站」那种联想——猜错比不补更烦。
    /// </summary>
    public static string? CompleteHost(string raw)
    {
        var q = raw.Trim();
        if (q.Length < 2 || q.Contains(' ') || q.Contains('/')) return null;

        var lower = q.ToLowerInvariant();
        foreach (var host in BrandHosts)
            if (host.StartsWith(lower, StringComparison.Ordinal) &&
                !string.Equals(host, lower, StringComparison.Ordinal))
                return host;

        return null;
    }
}
