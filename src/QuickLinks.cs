using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TLBrowser;

/// <summary>新标签页上的一枚快捷入口。</summary>
internal sealed class QuickLink
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>首字母缩写，画在磁贴的圆角方块里（没有 favicon 时的兜底）。
    /// ⚠️ 必须显式标 JsonPropertyName —— 网页脚本按小写 initials 取，
    /// 漏了就会序列化成 "Initials"，JS 拿到 undefined，磁贴只会显示 "··"。</summary>
    [JsonPropertyName("initials")]
    public string Initials
    {
        get
        {
            var src = string.IsNullOrWhiteSpace(Title) ? SafeHost : Title;
            var letters = new string(src.Where(char.IsLetterOrDigit).Take(2).ToArray());
            return letters.Length > 0 ? letters.ToUpperInvariant() : "··";
        }
    }

    private string SafeHost
    {
        get
        {
            try
            {
                var h = new Uri(Url).Host;
                return h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h;
            }
            catch { return "?"; }
        }
    }
}

/// <summary>
/// 新标签页的快捷入口集合。落盘成**人类可读**的 JSON，
/// 用户想手改就手改，程序每次启动重新读。
/// </summary>
internal sealed class QuickLinks
{
    private static string Path => System.IO.Path.Combine(Brand.AppDataDir, "quicklinks.json");

    /// <summary>
    /// 单实例共享，而 WebView2 的事件回调不保证在 UI 线程上
    /// （WebResourceRequested / WebMessageReceived 都可能来自内核线程），
    /// 读写都得过这把锁，免得两个标签页同时落盘把文件写坏。
    /// </summary>
    private static readonly object Gate = new();

    public List<QuickLink> Items { get; private set; } = Defaults();

    private static List<QuickLink> Defaults() => new()
    {
        new QuickLink { Title = "tlstudio.cn", Url = Brand.SiteTl },
        new QuickLink { Title = "tldoublerstudio.cn", Url = Brand.SiteDoubler },
    };

    public static QuickLinks Load()
    {
        lock (Gate)
        {
            var q = new QuickLinks();
            try
            {
                if (!File.Exists(Path)) { q.Save(); return q; }
                var txt = File.ReadAllText(Path, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<QuickLink>>(txt);
                if (items is { Count: > 0 }) q.Items = items;
            }
            catch
            {
                // 文件坏了就退回默认两个官网，别让主页开天窗
                q.Items = Defaults();
            }
            return q;
        }
    }

    public void Save()
    {
        try
        {
            var opt = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            lock (Gate)
                File.WriteAllText(Path, JsonSerializer.Serialize(Items, opt), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>新增一枚。同地址只保留一个，标题空就用域名兜底。</summary>
    public bool Add(string title, string url)
    {
        url = Normalize(url);
        if (url.Length == 0) return false;

        lock (Gate)
        {
            if (Items.Any(i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (string.IsNullOrWhiteSpace(title))
            {
                try { title = new Uri(url).Host; } catch { title = url; }
            }
            Items.Add(new QuickLink { Title = title.Trim(), Url = url });
        }
        Save();
        return true;
    }

    public bool Remove(string url)
    {
        lock (Gate)
        {
            var hit = Items.FirstOrDefault(i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));
            if (hit is null) return false;
            Items.Remove(hit);
        }
        Save();
        return true;
    }

    /// <summary>补全协议。用户手输 `tlstudio.cn` 也能用，但必须是像域名的东西。</summary>
    public static string Normalize(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) &&
               (u.Scheme == "http" || u.Scheme == "https") ? s : "";
    }
}
