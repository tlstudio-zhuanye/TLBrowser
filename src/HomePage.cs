using System.Text;
using System.Text.Json;

namespace TLBrowser;

/// <summary>
/// 新标签页（主页）—— **运行时生成，不往磁盘写任何 .html**。
///
/// 之前的做法是：把内嵌的 home.html 原样落到 LOCALAPPDATA 下，页面再去 file:// 打开它。
/// 两个问题：
///   1. 写完就固定了，切界面语言时网页不会跟着变（用户报的 bug）；
///   2. 磁盘上躺着一个可被其它程序改写的首页文件，本身就是攻击面。
///
/// 现在改成：主页挂在一个**永不解析的虚拟域名**上（.invalid 是 RFC 2606 保留后缀），
/// 由 WebView2 的 WebResourceRequested 在**内存里**直接回给内核 ——
/// 磁盘上没有任何文件，外部程序无从下手；内容每次都按当前语言现场生成，切语言立刻生效。
/// </summary>
internal static class HomePage
{
    /// <summary>
    /// 虚拟域名。用 .invalid 后缀（RFC 2606 保留，保证任何 DNS 都解析不出结果），
    /// 这样即便拦截器哪天没挂上，也绝不会真的跑到外网去。
    /// </summary>
    public const string Host = "tlstudio.invalid";
    public const string Origin = "https://" + Host;
    public const string Url = Origin + "/newtab";
    public const string LogoUrl = Origin + "/logo.png";

    private const string ResTemplate = "TLBrowser.assets.home.template.html";

    private static QuickLinks? _links;
    public static QuickLinks Links => _links ??= QuickLinks.Load();

    /// <summary>拼出本次要交给内核的 HTML。语言、快捷入口、品牌地址都是现场注入的。</summary>
    public static string Build()
    {
        var tpl = Brand.ReadTextResource(ResTemplate);
        if (tpl.Length == 0) return Fallback();

        var i18n = new Dictionary<string, string>
        {
            ["title"] = Lang.T("home.title"),
            ["ph"] = Lang.T("sug.placeholder"),
            ["go"] = Lang.T("sug.search"),
            ["hint"] = Lang.T("home.hint"),
            ["goTo"] = Lang.T("sug.navigate"),
            ["official"] = Lang.T("sug.brand"),
            ["searchWith"] = Lang.T("home.searchWith"),
            ["addTile"] = Lang.T("home.addTile"),
            ["removeTile"] = Lang.T("home.removeTile"),
            ["addTitle"] = Lang.T("home.addTitle"),
            ["nameLabel"] = Lang.T("home.nameLabel"),
            ["urlLabel"] = Lang.T("home.urlLabel"),
            ["confirm"] = Lang.T("common.ok"),
            ["cancel"] = Lang.T("common.cancel"),
            ["emptyName"] = Lang.T("home.emptyName"),
            ["badUrl"] = Lang.T("home.badUrl"),
            ["removed"] = Lang.T("home.removed"),
            ["tilesLabel"] = Lang.T("home.tilesLabel"),
        };

        var json = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        return tpl
            .Replace("%%LANG%%", Lang.T("home.lang"))
            .Replace("%%TITLE%%", Html(Lang.T("home.title")))
            .Replace("%%I18N%%", SafeJson(i18n, json))
            .Replace("%%LINKS%%", SafeJson(Links.Items, json))
            .Replace("%%SEARCH%%", Brand.SearchHostUrl)
            .Replace("%%SITE_TL%%", Brand.SiteTl)
            .Replace("%%SITE_DOUBLER%%", Brand.SiteDoubler)
            .Replace("%%VERSION%%", Brand.AppVersion);
    }

    /// <summary>模板读不出来时的兜底：宁可简陋，也别白屏。</summary>
    private static string Fallback()
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Lang.T("home.lang")).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(Html(Lang.T("home.title"))).Append("</title></head><body style=\"background:#fff\">");
        sb.Append("<form style=\"margin:80px auto;max-width:560px\" action=\"").Append(Brand.SearchHostUrl).Append("\" method=\"get\">");
        sb.Append("<input name=\"q\" style=\"width:80%\" placeholder=\"").Append(Html(Lang.T("sug.placeholder"))).Append("\">");
        sb.Append("<button type=\"submit\">").Append(Html(Lang.T("sug.search"))).Append("</button></form>");
        sb.Append("<p style=\"text-align:center\"><a href=\"").Append(Brand.SiteTl).Append("\">").Append(Brand.SiteTl).Append("</a></p>");
        sb.Append("<p style=\"text-align:center\"><a href=\"").Append(Brand.SiteDoubler).Append("\">").Append(Brand.SiteDoubler).Append("</a></p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// 主页自检：生成的页面里必须还是那三个写死的地址。
    /// 以前靠「比对磁盘文件」来发现被篡改，现在没有文件了 ——
    /// 改成校验**生成器自己**的输出，防止我们哪天把搜索引擎换成别的。
    /// </summary>
    public static bool SelfTest(out string detail)
    {
        var html = Build();

        // 1) 三个写死的地址必须都在
        var need = new[] { Brand.SearchHostUrl, Brand.SiteTl, Brand.SiteDoubler };
        var missing = need.Where(s => !html.Contains(s, StringComparison.Ordinal)).ToList();

        // 2) 模板里不该有没被替换掉的占位符 —— 漏了一个，页面上就会直接印出 %%XXX%%
        var leftover = System.Text.RegularExpressions.Regex
            .Matches(html, @"%%[A-Z_]+%%")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        detail = (missing.Count, leftover.Count) switch
        {
            (0, 0) => "主页内容自检通过：搜索引擎与两个官网地址都在，占位符全部替换",
            (0, _) => "主页占位符没替换干净：" + string.Join(" / ", leftover),
            (_, 0) => "主页内容自检失败，缺少：" + string.Join(" / ", missing),
            _ => "主页自检失败，缺少 " + string.Join(" / ", missing) +
                 "；占位符没替换：" + string.Join(" / ", leftover),
        };
        return missing.Count == 0 && leftover.Count == 0;
    }

    private static string Html(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// 序列化成要塞进 &lt;script&gt; 里的 JSON。
    /// 快捷入口的标题和网址是**用户自己填的**，如果原样输出，一句
    /// "&lt;/script&gt;&lt;script&gt;…" 就能截断脚本块，等于给自己开了个 XSS 口子。
    /// 所以保留中文可读性（UnsafeRelaxedJsonEscaping），但把 &lt; &gt; &amp; 转义掉。
    /// </summary>
    private static string SafeJson<T>(T value, JsonSerializerOptions opt) =>
        JsonSerializer.Serialize(value, opt)
            .Replace("<", "\\u003c")
            .Replace(">", "\\u003e")
            .Replace("&", "\\u0026");
}
