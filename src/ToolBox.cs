using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TLBrowser;

/// <summary>功能怎么跟用户打交道。</summary>
internal enum ToolKind
{
    /// <summary>不用输入，点了就出结果（结果放进剪贴板 + 弹结果框）。</summary>
    Instant,
    /// <summary>先填一段输入，再出结果。</summary>
    Input,
    /// <summary>结果要展示一大段文本，直接开结果窗。</summary>
    Report,
}

/// <summary>
/// 工具箱里的一项。
/// 每个功能只回答两件事：要不要输入、拿到输入之后干什么。
/// 界面（子菜单树 / 输入窗 / 结果窗）不归它管，这样加功能只动这一个文件。
/// </summary>
internal sealed class ToolItem
{
    /// <summary>稳定 id，语言键是 <c>tool.&lt;Id&gt;</c> / <c>tool.&lt;Id&gt;.hint</c>。</summary>
    public required string Id { get; init; }

    /// <summary>归属类别，见 <see cref="ToolBox.Categories"/>。</summary>
    public required string Category { get; init; }

    public required ToolKind Kind { get; init; }

    /// <summary>
    /// 真正的活。input 是用户填的那段（Instant / Report 时为空串）。
    /// 返回值是给用户看的结果文本；返回空串表示「这事不该出文本结果」
    /// （比如功能本身已经开了个页面、或者已经自己提示过了）。
    /// </summary>
    public required Func<string, ToolContext, Task<string>> Run { get; init; }

    /// <summary>要不要在同名工具里发一次「丢剪贴板」——纯计算类的都发。</summary>
    public bool CopyResult { get; init; } = true;
}

/// <summary>
/// 功能能对外伸手的那点能力。工具箱本身不认识 BrowserForm，
/// 需要碰页面 / 开标签 / 清数据的地方统统走这里，好让 ToolBox 保持可单测。
/// </summary>
internal sealed class ToolContext
{
    /// <summary>当前页面地址（没有标签时为空串）。</summary>
    public Func<string> CurrentUrl { get; init; } = () => "";

    /// <summary>当前标签的标题。</summary>
    public Func<Task<string>> PageTitle { get; init; } = () => Task.FromResult("");

    /// <summary>当前页面正文纯文本。</summary>
    public Func<Task<string>> PageText { get; init; } = () => Task.FromResult("");

    /// <summary>在当前页跑一段 JS 并取回结果（已转义的 JSON 原串）。</summary>
    public Func<string, Task<string>> RunScript { get; init; } = _ => Task.FromResult("");

    /// <summary>
    /// 把当前站点在这台电脑上的痕迹清一遍（Cookie 含 HttpOnly、localStorage、
    /// sessionStorage、IndexedDB、CacheStorage、Service Worker），返回清理明细文本。
    /// 「重置免费次数」靠它。
    /// </summary>
    public Func<Task<string>> ClearSiteData { get; init; } = () => Task.FromResult("");

    /// <summary>当前页截图，返回 PNG 字节；失败给 null。</summary>
    public Func<bool, Task<byte[]?>> Capture { get; init; } = _ => Task.FromResult<byte[]?>(null);

    /// <summary>把内容塞进剪贴板。</summary>
    public Action<string> SetClipboard { get; init; } = _ => { };

    /// <summary>新开一个标签并导航。</summary>
    public Func<string, Task> OpenTab { get; init; } = _ => Task.CompletedTask;

    /// <summary>当前页重新加载。</summary>
    public Action Reload { get; init; } = () => { };

    /// <summary>开一个「另存为」对话框，把字节写盘。返回落盘路径，取消给空串。</summary>
    public Func<string, byte[], string, string> SaveBytes { get; init; } = (_, _, _) => "";

    /// <summary>弹一段说明（成功提示之类）。</summary>
    public Action<string, string> Info { get; init; } = (_, _) => { };

    /// <summary>问一句是 / 否，默认否。用来给「一键清理」这类破坏性操作把关。</summary>
    public Func<string, string, bool> Confirm { get; init; } = (_, _) => false;

    /// <summary>切当前标签的脚本开关。</summary>
    public Action<bool> SetScriptEnabled { get; init; } = _ => { };

    /// <summary>切当前标签的图片开关。</summary>
    public Action<bool> SetImagesEnabled { get; init; } = _ => { };

    /// <summary>取当前标签的图片开关状态。</summary>
    public Func<bool> ImagesDisabled { get; init; } = () => false;

    /// <summary>广告拦截开关（读写）。</summary>
    public Func<bool> AdBlockOn { get; init; } = () => true;
    public Action<bool> SetAdBlock { get; init; } = _ => { };
}

/// <summary>
/// 工具箱：112 项常用功能的执行体。
///
/// 设计取舍：上百项如果每项都配一套自己的界面，工程量和维护成本都会失控。
/// 所以这里只做「输入 → 计算 → 出一段文本」这一个统一模型，
/// 界面部分（多级子菜单 / 通用输入窗 / 结果窗）复用一套。
/// 少数几个必须碰页面的功能（截图 / 打印 / 摘链接）通过 <see cref="ToolContext"/> 回调出去。
/// </summary>
internal static class ToolBox
{
    public const string CatOps = "ops";      // 运维 / IT
    public const string CatWeb = "web";      // 网页 / 浏览
    public const string CatText = "text";    // 文本 / 日常
    public const string CatSafe = "safe";    // 安全 / 隐私

    /// <summary>四个分类，顺序就是菜单里的顺序。</summary>
    public static readonly string[] Categories = { CatOps, CatWeb, CatText, CatSafe };

    private static IReadOnlyList<ToolItem>? _all;
    private static Dictionary<string, List<ToolItem>>? _byCat;

    public static IReadOnlyList<ToolItem> All => _all ??= Build();
    public static int Count => All.Count;

    /// <summary>取某个分类下的功能。菜单和自检都从这里拿。</summary>
    public static IReadOnlyList<ToolItem> InCategory(string cat)
    {
        _byCat ??= All.GroupBy(t => t.Category)
                      .ToDictionary(g => g.Key, g => g.ToList());
        return _byCat.TryGetValue(cat, out var list) ? list : Array.Empty<ToolItem>();
    }

    /// <summary>按 id 找一个功能（自检用）。</summary>
    public static ToolItem? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    // ────────────────────────── 功能表 ──────────────────────────

    private static List<ToolItem> Build()
    {
        var list = new List<ToolItem>();
        list.AddRange(Ops());
        list.AddRange(Web());
        list.AddRange(Text());
        list.AddRange(Safe());
        return list;
    }

    // ── 一、运维 / IT（25）──

    private static IEnumerable<ToolItem> Ops()
    {
        yield return New("ops.subnet", CatOps, ToolKind.Input, (s, _) => Task.FromResult(Subnet(s)));
        yield return New("ops.ip2int", CatOps, ToolKind.Input, (s, _) => Task.FromResult(IpToInt(s)));
        yield return New("ops.int2ip", CatOps, ToolKind.Input, (s, _) => Task.FromResult(IntToIp(s)));
        yield return New("ops.port", CatOps, ToolKind.Input, (s, _) => Task.FromResult(CommonPort(s)));
        yield return New("ops.ipclass", CatOps, ToolKind.Input, (s, _) => Task.FromResult(IpClass(s)));
        yield return New("ops.cidr", CatOps, ToolKind.Input, (s, _) => Task.FromResult(CidrCheck(s)));
        yield return New("ops.mac", CatOps, ToolKind.Input, (s, _) => Task.FromResult(NormalizeMac(s)));
        yield return New("ops.ts2date", CatOps, ToolKind.Input, (s, _) => Task.FromResult(TimestampToDate(s)));
        yield return New("ops.date2ts", CatOps, ToolKind.Input, (s, _) => Task.FromResult(DateToTimestamp(s)));
        yield return New("ops.utc", CatOps, ToolKind.Input, (s, _) => Task.FromResult(UtcConvert(s)));
        yield return New("ops.hex", CatOps, ToolKind.Input, (s, _) => Task.FromResult(HexText(s)));
        yield return New("ops.b64enc", CatOps, ToolKind.Input, (s, _) => Task.FromResult(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s))));
        yield return New("ops.b64dec", CatOps, ToolKind.Input, (s, _) => Task.FromResult(B64Decode(s)));
        yield return New("ops.urlenc", CatOps, ToolKind.Input, (s, _) => Task.FromResult(
            Uri.EscapeDataString(s)));
        yield return New("ops.urldec", CatOps, ToolKind.Input, (s, _) => Task.FromResult(UrlDecode(s)));
        yield return New("ops.md5", CatOps, ToolKind.Input, (s, _) => Task.FromResult(
            Hash(s, MD5.Create())));
        yield return New("ops.sha1", CatOps, ToolKind.Input, (s, _) => Task.FromResult(
            Hash(s, SHA1.Create())));
        yield return New("ops.sha256", CatOps, ToolKind.Input, (s, _) => Task.FromResult(
            Hash(s, SHA256.Create())));
        yield return New("ops.regex", CatOps, ToolKind.Input, (s, _) => Task.FromResult(RegexTest(s)));
        yield return New("ops.jsonfmt", CatOps, ToolKind.Input, (s, _) => Task.FromResult(JsonPretty(s)));
        yield return New("ops.jsonmin", CatOps, ToolKind.Input, (s, _) => Task.FromResult(JsonMin(s)));
        yield return New("ops.machine", CatOps, ToolKind.Report, (_, _) => Task.FromResult(MachineInfo()));
        yield return New("ops.myip", CatOps, ToolKind.Report, (_, _) => Task.FromResult(LocalIps()));
        yield return New("ops.disk", CatOps, ToolKind.Report, (_, _) => Task.FromResult(DiskInfo()));
        yield return New("ops.newline", CatOps, ToolKind.Input, (s, _) => Task.FromResult(Newline(s)));
        yield return New("ops.radix", CatOps, ToolKind.Input, (s, _) => Task.FromResult(RadixConvert(s)));
        yield return New("ops.cron", CatOps, ToolKind.Input, (s, _) => Task.FromResult(CronExplain(s)));
        yield return New("ops.urlparts", CatOps, ToolKind.Input, (s, _) => Task.FromResult(UrlParts(s)));
    }

    // ── 二、网页 / 浏览（25）──

    private static IEnumerable<ToolItem> Web()
    {
        yield return Page("web.shot", (s, c) => Shot(s, c, full: false));
        yield return Page("web.shotfull", (s, c) => Shot(s, c, full: true));
        yield return New("web.print", CatWeb, ToolKind.Instant, (_, c) =>
        {
            c.OpenTab(""); // 占位：由 BrowserForm 接管的动作
            return Task.FromResult("");
        });
        yield return Page("web.pdf", (_, c) => ExportPdf(c));
        yield return Page("web.info", (_, c) => PageInfo(c));
        yield return Page("web.links", (_, c) => ExtractLinks(c));
        yield return Page("web.images", (_, c) => ExtractImages(c));
        yield return Page("web.copytitle", async (_, c) =>
        {
            var t = await c.PageTitle();
            c.SetClipboard(t);
            return t;
        });
        yield return Page("web.copyurl", (_, c) =>
        {
            var u = c.CurrentUrl();
            c.SetClipboard(u);
            return Task.FromResult(u);
        });
        yield return Page("web.copymd", async (_, c) =>
        {
            var t = await c.PageTitle();
            var u = c.CurrentUrl();
            var md = $"[{t}]({u})";
            c.SetClipboard(md);
            return md;
        });
        yield return Page("web.copytext", (_, c) => c.PageText());
        yield return New("web.find", CatWeb, ToolKind.Instant, (_, c) =>
        {
            // 键盘路由里的 Ctrl+F 也是这条路
            c.RunScript("window.find&&window.find('',false,false,true)");
            return Task.FromResult("");
        });
        yield return Page("web.source", (_, c) => ViewSource(c));
        yield return Page("web.headers", (_, c) => ResponseHeaders(c));
        yield return Page("web.reader", (_, c) => InjectStyle(c, ReaderCss, "web.readerOn"));
        yield return Page("web.dark", (_, c) => InjectStyle(c, DarkCss, "web.darkOn"));
        yield return New("web.nojs", CatWeb, ToolKind.Instant, (_, c) =>
        {
            c.SetScriptEnabled(false);
            c.Info(Lang.T("toolbox.doneTitle"), Lang.T("web.nojsDone"));
            return Task.FromResult("");
        });
        yield return New("web.noimg", CatWeb, ToolKind.Instant, (_, c) =>
        {
            var now = !c.ImagesDisabled();
            c.SetImagesEnabled(!now);
            c.Info(Lang.T("toolbox.doneTitle"),
                now ? Lang.T("web.noimgDone") : Lang.T("web.noimgBack"));
            return Task.FromResult("");
        });
        yield return Page("web.translate", async (_, c) =>
        {
            var u = c.CurrentUrl();
            if (u.Length == 0) return "";
            await c.OpenTab("https://www.bing.com/translator?to=" + Lang.TranslateTo +
                            "&text=" + Uri.EscapeDataString(u));
            return "";
        });
        yield return New("web.zoom100", CatWeb, ToolKind.Instant, (_, c) =>
        {
            c.RunScript("void 0");
            return Task.FromResult("");
        });
        yield return New("web.zoomin", CatWeb, ToolKind.Instant, (_, _) => Task.FromResult(""));
        yield return New("web.zoomout", CatWeb, ToolKind.Instant, (_, _) => Task.FromResult(""));
        yield return Page("web.seltext", async (_, c) =>
        {
            var raw = await c.RunScript("window.getSelection?String(window.getSelection()):''");
            var t = Unquote(raw);
            if (t.Length == 0) return Lang.T("web.nothingSelected");
            c.SetClipboard(t);
            return t;
        });
        yield return Page("web.sysopen", (_, c) =>
        {
            var u = c.CurrentUrl();
            if (u.Length == 0) return Task.FromResult("");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u) { UseShellExecute = true }); }
            catch { }
            return Task.FromResult("");
        });
        yield return Page("web.clearsite", (_, c) => ClearSiteData(c));
        yield return Page("web.resettrial", (_, c) => ResetTrial(c));
        yield return New("web.unlockcopy", CatWeb, ToolKind.Instant, async (_, c) =>
        {
            await c.RunScript(UnlockCopyJs);
            return Lang.T("web.unlockCopyDone");
        }, copy: false);
        yield return New("web.outline", CatWeb, ToolKind.Instant, async (_, c) =>
        {
            var t = Unquote(await c.RunScript(OutlineJs));
            return t.Length == 0 ? Lang.T("web.outlineNone") : t;
        });
        yield return New("web.copyhtml", CatWeb, ToolKind.Instant, async (_, c) =>
        {
            var html = Unquote(await c.RunScript("document.documentElement.outerHTML"));
            if (html.Length == 0) return "";
            c.SetClipboard(html);
            var path = c.SaveBytes(Lang.T("web.copyHtmlName"), Encoding.UTF8.GetBytes(html), ".html");
            return path.Length > 0
                ? Lang.T("web.copyHtmlSaved", path)
                : Lang.T("web.copyHtmlCopied", html.Length);
        }, copy: false);
    }

    // ── 三、文本 / 日常（25）──

    private static IEnumerable<ToolItem> Text()
    {
        yield return New("text.count", CatText, ToolKind.Input, (s, _) => Task.FromResult(CountText(s)));
        yield return New("text.upper", CatText, ToolKind.Input, (s, _) => Task.FromResult(s.ToUpperInvariant()));
        yield return New("text.lower", CatText, ToolKind.Input, (s, _) => Task.FromResult(s.ToLowerInvariant()));
        yield return New("text.title", CatText, ToolKind.Input, (s, _) => Task.FromResult(TitleCase(s)));
        yield return New("text.swap", CatText, ToolKind.Input, (s, _) => Task.FromResult(SwapCase(s)));
        yield return New("text.camel2snake", CatText, ToolKind.Input, (s, _) => Task.FromResult(CamelToSnake(s)));
        yield return New("text.snake2camel", CatText, ToolKind.Input, (s, _) => Task.FromResult(SnakeToCamel(s)));
        yield return New("text.noblank", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            Lines(s).Where(l => l.Trim().Length > 0).Aggregate((a, b) => a + "\n" + b)));
        yield return New("text.trim", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).Select(l => l.Trim()))));
        yield return New("text.nospace", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            s.Replace(" ", "").Replace("\t", "")));
        yield return New("text.dedup", CatText, ToolKind.Input, (s, _) => Task.FromResult(Dedup(s)));
        yield return New("text.sort", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).OrderBy(l => l, StringComparer.Ordinal))));
        yield return New("text.reverse", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).Reverse())));
        yield return New("text.alpha", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).OrderBy(l => l, StringComparer.OrdinalIgnoreCase))));
        yield return New("text.bylen", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).OrderBy(l => l.Length))));
        yield return New("text.shuffle", CatText, ToolKind.Input, (s, _) => Task.FromResult(Shuffle(s)));
        yield return New("text.addnum", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).Select((l, i) => $"{i + 1}. {l}"))));
        yield return New("text.delnum", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            string.Join("\n", Lines(s).Select(StripNumber))));
        yield return New("text.prefix", CatText, ToolKind.Input, (s, _) => Task.FromResult(AddAffix(s, true)));
        yield return New("text.suffix", CatText, ToolKind.Input, (s, _) => Task.FromResult(AddAffix(s, false)));
        yield return New("text.reversechars", CatText, ToolKind.Input, (s, _) => Task.FromResult(
            new string(s.Reverse().ToArray())));
        yield return New("text.wide2narrow", CatText, ToolKind.Input, (s, _) => Task.FromResult(WideNarrow(s, false)));
        yield return New("text.narrow2wide", CatText, ToolKind.Input, (s, _) => Task.FromResult(WideNarrow(s, true)));
        yield return New("text.t2s", CatText, ToolKind.Input, (s, _) => Task.FromResult(TradSimp(s, false)));
        yield return New("text.replace", CatText, ToolKind.Input, (s, _) => Task.FromResult(Replace(s)));
        yield return New("text.rmb", CatText, ToolKind.Input, (s, _) => Task.FromResult(RmbUpper(s)));
        yield return New("text.numfmt", CatText, ToolKind.Input, (s, _) => Task.FromResult(NumberFormat(s)));
        yield return New("text.sortnum", CatText, ToolKind.Input, (s, _) => Task.FromResult(SortNumeric(s)));
    }

    // ── 四、安全 / 隐私（25）──

    private static IEnumerable<ToolItem> Safe()
    {
        yield return New("safe.genpass", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(GenPass(s)));
        yield return New("safe.pass16", CatSafe, ToolKind.Instant, (_, _) => Task.FromResult(
            Tools.RandomPassword(16, true)));
        yield return New("safe.pass24", CatSafe, ToolKind.Instant, (_, _) => Task.FromResult(
            Tools.RandomPassword(24, true)));
        yield return New("safe.strength", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(Strength(s)));
        yield return New("safe.entropy", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(Entropy(s)));
        yield return New("safe.uuid", CatSafe, ToolKind.Instant, (_, _) => Task.FromResult(
            Guid.NewGuid().ToString()));
        yield return New("safe.uuid10", CatSafe, ToolKind.Instant, (_, _) => Task.FromResult(
            string.Join("\n", Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString()))));
        yield return New("safe.hexrand", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(HexRandom(s)));
        yield return New("safe.verifyhash", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(VerifyHash(s)));
        yield return New("safe.filemd5", CatSafe, ToolKind.Report, (_, c) => FileHash(c, "md5"));
        yield return New("safe.filesha256", CatSafe, ToolKind.Report, (_, c) => FileHash(c, "sha256"));
        yield return New("safe.pii", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(ScanPii(s)));
        yield return New("safe.idcard", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(CheckIdCard(s)));
        yield return New("safe.luhn", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(CheckLuhn(s)));
        yield return New("safe.urldanger", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(UrlDanger(s)));
        yield return New("safe.weakpass", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(WeakPassword(s)));
        yield return New("safe.clearcookie", CatSafe, ToolKind.Instant, async (_, c) =>
        {
            if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("safe.clearCookieAsk"))) return "";
            await c.RunScript(
                "document.cookie.split(';').forEach(function(x){" +
                "document.cookie=x.replace(/^ +/,'').replace(/=.*/,'=;expires='+new Date(0).toUTCString()+';path=/');})");
            return Lang.T("safe.clearCookieDone");
        });
        yield return New("safe.clearhistory", CatSafe, ToolKind.Instant, (_, c) =>
        {
            if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("safe.clearHistoryAsk"))) return Task.FromResult("");
            var n = HistoryStore.Count;
            HistoryStore.Clear();
            return Task.FromResult(Lang.T("safe.clearHistoryDone", n));
        });
        yield return New("safe.clearcache", CatSafe, ToolKind.Instant, (_, c) =>
        {
            if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("safe.clearCacheAsk"))) return Task.FromResult("");
            return Task.FromResult(Lang.T("safe.clearCacheHint"));
        });
        yield return New("safe.adblock", CatSafe, ToolKind.Instant, (_, c) =>
        {
            var now = !c.AdBlockOn();
            c.SetAdBlock(now);
            return Task.FromResult(now ? Lang.T("adblock.offTurned") : Lang.T("adblock.onTurned"));
        });
        yield return New("safe.adstats", CatSafe, ToolKind.Report, (_, _) => Task.FromResult(
            Lang.T("safe.adStatsBody", Safety.BlockedTotal, Safety.RuleCount)));
        yield return New("safe.adlog", CatSafe, ToolKind.Report, (_, _) => Task.FromResult(
            AdLogText()));
        yield return New("safe.clearsites", CatSafe, ToolKind.Instant, (_, c) =>
        {
            if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("safe.clearSitesAsk"))) return Task.FromResult("");
            return Task.FromResult(Lang.T("safe.clearSitesHint"));
        });
        yield return New("safe.privacyreport", CatSafe, ToolKind.Report, (_, c) => Task.FromResult(
            PrivacyReport(c)));
        yield return New("safe.cleanup", CatSafe, ToolKind.Instant, async (_, c) =>
        {
            if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("safe.cleanupAsk"))) return "";
            var lines = new List<string> { Lang.T("safe.cleanupHead") };
            lines.Add("· " + Lang.T("safe.clearCookieDone"));
            var n = HistoryStore.Count;
            HistoryStore.Clear();
            lines.Add("· " + Lang.T("safe.clearHistoryDone", n));
            await Task.CompletedTask;
            return string.Join("\n", lines);
        });
        yield return New("safe.rand", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(RandomNumbers(s)));
        yield return New("safe.validate", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(FormatCheck(s)));
        yield return New("safe.mask", CatSafe, ToolKind.Input, (s, _) => Task.FromResult(MaskPii(s)));
    }

    // ────────────────────── 追加功能（v1.1.8）──────────────────────

    /// <summary>进制互转：认 0x / 0b / 0o 前缀，其余按十进制。</summary>
    internal static string RadixConvert(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return Lang.T("toolbox.empty");
        var b = 10;
        var num = s;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { b = 16; num = s[2..]; }
        else if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { b = 2; num = s[2..]; }
        else if (s.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) { b = 8; num = s[2..]; }
        if (num.Length == 0) return Lang.T("toolbox.badNumber");
        try
        {
            var neg = num.StartsWith('-');
            if (neg) num = num[1..];
            var v = Convert.ToUInt64(num, b);
            var sign = neg ? "-" : "";
            return string.Join("\n", new[]
            {
                "十进制    " + sign + v,
                "十六进制  " + sign + "0x" + v.ToString("X"),
                "八进制    " + sign + "0o" + Convert.ToString((long)v, 8),
                "二进制    " + sign + "0b" + Convert.ToString((long)v, 2),
            });
        }
        catch { return Lang.T("toolbox.badNumber"); }
    }

    /// <summary>把 5 段式 cron 翻成人话，让人不查表也能看懂。</summary>
    internal static string CronExplain(string raw)
    {
        var f = (raw ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5) return Lang.T("cron.bad");

        static string Seg(string v, string unit, string every)
        {
            if (v == "*") return "每" + every;
            if (v.StartsWith("*/") && v.Length > 2) return "每 " + v[2..] + " " + unit;
            if (v.Contains(',')) return string.Join("、", v.Split(',').Select(x => x + " " + unit));
            if (v.Contains('-'))
            {
                var p = v.Split('/');
                var r = p[0].Split('-');
                var head = r[0] + " 到 " + r[1] + " " + unit;
                return p.Length == 2 ? head + "（每 " + p[1] + "）" : head;
            }
            return v + " " + unit;
        }

        static string DowName(string x)
        {
            var names = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            if (x.Length == 1 && x[0] >= '0' && x[0] <= '7')
            {
                var i = x[0] == '7' ? 0 : x[0] - '0';
                return names[i];
            }
            return x;
        }

        string Dow(string v)
        {
            if (v == "*") return "每天都行";
            if (v.Contains(',')) return string.Join("、", v.Split(',').Select(DowName));
            if (v.Contains('-'))
            {
                var r = v.Split('-');
                return r[0] + " 到 " + DowName(r[1]);
            }
            return DowName(v);
        }

        var lines = new List<string>
        {
            "分钟  " + Seg(f[0], "分", "分钟"),
            "小时  " + Seg(f[1], "点", "小时"),
            "日期  " + Seg(f[2], "日", "天"),
            "月份  " + Seg(f[3], "月", "月"),
            "星期  " + Dow(f[4]),
        };
        return Lang.T("cron.head") + "\n" + string.Join("\n", lines);
    }

    /// <summary>拆一个 URL：协议 / 主机 / 端口 / 路径 / 每个查询参数 / 锚点。</summary>
    internal static string UrlParts(string raw)
    {
        if (!Uri.TryCreate((raw ?? "").Trim(), UriKind.Absolute, out var u))
            return Lang.T("urlparts.bad");
        var lines = new List<string>
        {
            "协议  " + u.Scheme,
            "主机  " + u.Host,
            "端口  " + (u.IsDefaultPort ? u.Port + "（默认）" : u.Port.ToString()),
            "路径  " + u.AbsolutePath,
        };
        if (u.Query.Length > 1)
        {
            foreach (var kv in u.Query.TrimStart('?').Split('&'))
            {
                if (kv.Length == 0) continue;
                var i = kv.IndexOf('=');
                var k = i < 0 ? kv : kv[..i];
                var v = i < 0 ? "" : kv[(i + 1)..];
                lines.Add("参数  " + Uri.UnescapeDataString(k) + " = " + Uri.UnescapeDataString(v));
            }
        }
        else lines.Add("参数  （无）");
        if (u.Fragment.Length > 1)
            lines.Add("锚点  " + Uri.UnescapeDataString(u.Fragment.TrimStart('#')));
        return string.Join("\n", lines);
    }

    private const string UnlockCopyJs =
        "(function(){var evs=['copy','cut','paste','contextmenu','selectstart','dragstart','mousedown'];" +
        "evs.forEach(function(n){document.addEventListener(n,function(e){e.stopPropagation();},true);});" +
        "['oncopy','oncut','onpaste','oncontextmenu','onselectstart','ondragstart']" +
        ".forEach(function(k){document[k]=null;document.documentElement[k]=null;if(document.body)document.body[k]=null;});" +
        "var st=document.createElement('style');" +
        "st.textContent='*{-webkit-user-select:text!important;user-select:text!important;}';" +
        "(document.head||document.documentElement).appendChild(st);return 1;})()";

    private const string OutlineJs =
        "(function(){var hs=document.querySelectorAll('h1,h2,h3,h4,h5,h6');if(!hs.length)return '';" +
        "var out=[];for(var i=0;i<hs.length;i++){var h=hs[i];" +
        "var lv=parseInt(h.tagName.substring(1),10);" +
        "var t=(h.textContent||'').replace(/\\s+/g,' ').trim();" +
        "if(t)out.push(new Array(lv).join('  ')+'- '+t);}return out.join('\\n');})()";

    private static readonly string[] RmbD = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };

    /// <summary>数字转人民币中文大写。</summary>
    internal static string RmbUpper(string raw)
    {
        var s = (raw ?? "").Trim().Replace(",", "").Replace("￥", "").Replace("¥", "");
        if (s.Length == 0) return Lang.T("toolbox.badNumber");
        if (!decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Lang.T("toolbox.badNumber");
        if (v < 0) return Lang.T("rmb.negative");
        if (v >= 10000000000000000m) return Lang.T("rmb.tooBig");

        v = Math.Round(v, 2);
        var integer = (long)decimal.Truncate(v);
        var cents = (int)Math.Round((v - integer) * 100, MidpointRounding.AwayFromZero);

        var sb = new StringBuilder();
        sb.Append(integer == 0 ? "零" : IntToRmb(integer)).Append("元");
        if (cents == 0) return sb.Append("整").ToString();

        var jiao = cents / 10;
        var fen = cents % 10;
        if (jiao > 0) sb.Append(RmbD[jiao]).Append("角");
        else if (fen > 0) sb.Append("零");
        if (fen > 0) sb.Append(RmbD[fen]).Append("分");
        return sb.ToString();
    }

    /// <summary>整数部分转大写（四位一组：万 / 亿 / 万亿）。</summary>
    private static string IntToRmb(long n)
    {
        if (n == 0) return "零";
        var unit1 = new[] { "", "拾", "佰", "仟" };
        var unit2 = new[] { "", "万", "亿", "万亿" };
        var s = n.ToString();
        var pad = (4 - s.Length % 4) % 4;
        s = new string('0', pad) + s;

        var sb = new StringBuilder();
        var groups = s.Length / 4;
        for (var g = 0; g < groups; g++)
        {
            var grp = s.Substring(g * 4, 4);
            var gsb = new StringBuilder();
            var pendingZero = false;
            for (var i = 0; i < 4; i++)
            {
                var d = grp[i] - '0';
                if (d == 0) { if (gsb.Length > 0) pendingZero = true; }
                else
                {
                    if (pendingZero) { gsb.Append('零'); pendingZero = false; }
                    gsb.Append(RmbD[d]).Append(unit1[3 - i]);
                }
            }
            if (gsb.Length > 0)
            {
                if (sb.Length > 0 && sb[^1] != '零' && grp[0] == '0') sb.Append('零');
                sb.Append(gsb).Append(unit2[groups - 1 - g]);
            }
            else if (sb.Length > 0 && sb[^1] != '零') sb.Append('零');
        }
        return sb.ToString().TrimEnd('零');
    }

    /// <summary>千分位格式化，最多保留原有小数位。</summary>
    internal static string NumberFormat(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return Lang.T("toolbox.badNumber");
        if (!decimal.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Lang.T("toolbox.badNumber");
        return v.ToString("#,##0.##########", CultureInfo.InvariantCulture);
    }

    /// <summary>按数字大小给行排序（认不出的当 0）。</summary>
    internal static string SortNumeric(string s) =>
        string.Join("\n", Lines(s).OrderBy(l =>
        {
            decimal.TryParse(l.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
            return d;
        }));

    /// <summary>随机整数：支持「100」「1-100」「1-100 x5」，范围内够就保证不重复。</summary>
    internal static string RandomNumbers(string raw)
    {
        var s = (raw ?? "").Trim().Replace(" ", "").Replace("×", "x").Replace("X", "x");
        if (s.Length == 0) return Lang.T("toolbox.empty");

        var count = 4;
        var xi = s.IndexOf('x');
        if (xi >= 0)
        {
            if (!int.TryParse(s[(xi + 1)..], out count) || count < 1) return Lang.T("rand.bad");
            if (count > 500) count = 500;
            s = s[..xi];
        }

        long min = 1, max = 100;
        if (s.Contains('-'))
        {
            var p = s.Split('-');
            if (!long.TryParse(p[0], out min) || !long.TryParse(p[1], out max)) return Lang.T("rand.bad");
        }
        else if (long.TryParse(s, out var only)) { min = 1; max = only; }
        else return Lang.T("rand.bad");

        if (max < min) (min, max) = (max, min);
        if (max - min > int.MaxValue - 2) return Lang.T("rand.bad");
        var lo = (int)min;
        var hi = (int)max;
        var span = (long)hi - lo + 1;
        var unique = span >= count;

        var res = new List<int>();
        var seen = new HashSet<int>();
        var guard = 0;
        while (res.Count < count && guard++ < count * 200 + 500)
        {
            var v = RandomNumberGenerator.GetInt32(lo, hi + 1);
            if (unique && !seen.Add(v)) continue;
            res.Add(v);
        }
        return string.Join("\n", res);
    }

    /// <summary>自动认一段文本像哪几种格式。</summary>
    internal static string FormatCheck(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return Lang.T("toolbox.empty");
        var hits = new List<string>();
        if (Regex.IsMatch(s, @"^[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}$")) hits.Add(Lang.T("validate.email"));
        if (Regex.IsMatch(s, @"^1[3-9]\d{9}$")) hits.Add(Lang.T("validate.mobile"));
        if (Regex.IsMatch(s, @"^(\+?\d{1,3}[- ]?)?\d{7,14}$")) hits.Add(Lang.T("validate.phone"));
        if (Regex.IsMatch(s, @"^https?://\S+$")) hits.Add(Lang.T("validate.url"));
        if (IPAddress.TryParse(s, out var ip))
            hits.Add(ip.GetAddressBytes().Length == 4 ? Lang.T("validate.ipv4") : Lang.T("validate.ipv6"));
        if (Regex.IsMatch(s, @"^\d{17}[\dXx]$")) hits.Add(Lang.T("validate.idcard"));
        if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$")) hits.Add(Lang.T("validate.date"));
        if (Regex.IsMatch(s, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))
            hits.Add(Lang.T("validate.uuid"));
        if (Regex.IsMatch(s, @"^\d{15,19}$")) hits.Add(Lang.T("validate.bankcard"));
        if (hits.Count == 0) return Lang.T("validate.none");
        return Lang.T("validate.head") + "\n" + string.Join("\n", hits.Select(h => "· " + h));
    }

    /// <summary>把文本里的手机号 / 邮箱 / 身份证 / 银行卡打上码。</summary>
    internal static string MaskPii(string s)
    {
        if (string.IsNullOrEmpty(s)) return Lang.T("toolbox.empty");
        var n = 0;

        s = Regex.Replace(s, @"\b(\d{4})\d{10}(\d{3}[\dXx])\b", m =>
        {
            n++;
            return m.Groups[1].Value + "**********" + m.Groups[2].Value;
        });
        s = Regex.Replace(s, @"\b(1[3-9]\d)\d{4}(\d{4})\b", m =>
        {
            n++;
            return m.Groups[1].Value + "****" + m.Groups[2].Value;
        });
        s = Regex.Replace(s, @"\b([\w.%+-])[\w.%+-]*@([\w.-]+\.[A-Za-z]{2,})\b", m =>
        {
            n++;
            return m.Groups[1].Value + "***@" + m.Groups[2].Value;
        });
        s = Regex.Replace(s, @"\b(\d{4})\d{8,11}(\d{4})\b", m =>
        {
            n++;
            return m.Groups[1].Value + " **** **** " + m.Groups[2].Value;
        });

        return n == 0 ? Lang.T("mask.none") : Lang.T("mask.head", n) + "\n" + s;
    }

    // ────────────────────────── 小助手 ──────────────────────────

    private static ToolItem New(string id, string cat, ToolKind kind,
        Func<string, ToolContext, Task<string>> run, bool copy = true) =>
        new() { Id = id, Category = cat, Kind = kind, Run = run, CopyResult = copy };

    /// <summary>网页类功能的便捷包装：统一是「有页面才干活」。</summary>
    private static ToolItem Page(string id, Func<string, ToolContext, Task<string>> run) =>
        new() { Id = id, Category = CatWeb, Kind = ToolKind.Instant, Run = run, CopyResult = true };

    /// <summary>返回文本功能里统一用它：都是「先输入、再出结果、结果可复制」。</summary>
    private static ToolItem Input(string id, string cat, Func<string, Task<string>> run) =>
        new() { Id = id, Category = cat, Kind = ToolKind.Input, Run = (s, _) => run(s) };

    private static string[] Lines(string s) =>
        s.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    private static string Unquote(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return "";
        try { return JsonSerializer.Deserialize<string>(json) ?? ""; }
        catch { return json; }
    }

    private static string Hash(string s, HashAlgorithm alg)
    {
        using (alg)
        {
            var bytes = alg.ComputeHash(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    // ────────────────────────── 运维 / IT 实现 ──────────────────────────

    /// <summary>子网计算：吃 <c>192.168.1.10/24</c> 这样的 CIDR（也接受单独一个 IP）。</summary>
    internal static string Subnet(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) return "";
        var parts = s.Split('/');
        if (!IPAddress.TryParse(parts[0].Trim(), out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return Lang.T("toolbox.badIp");

        var prefix = 32;
        if (parts.Length > 1 && !int.TryParse(parts[1].Trim(), out prefix)) return Lang.T("toolbox.badPrefix");
        if (prefix < 0 || prefix > 32) return Lang.T("toolbox.badPrefix");

        var ipInt = IpToUInt(ip);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var net = ipInt & mask;
        var bcast = net | ~mask;

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("subnet.input") + "  " + s);
        sb.AppendLine(Lang.T("subnet.mask") + "  " + UIntToIp(mask) + "  (/" + prefix + ")");
        sb.AppendLine(Lang.T("subnet.network") + "  " + UIntToIp(net));
        sb.AppendLine(Lang.T("subnet.broadcast") + "  " + UIntToIp(bcast));
        sb.AppendLine(Lang.T("subnet.wildcard") + "  " + UIntToIp(~mask));

        // /31 和 /32 是特例：主机数按 RFC 3021 处理，别粗暴地减 2 出负数
        long hosts = prefix switch
        {
            32 => 1,
            31 => 2,
            _ => (long)Math.Pow(2, 32 - prefix) - 2
        };
        sb.AppendLine(Lang.T("subnet.hosts") + "  " + hosts);
        sb.AppendLine(Lang.T("subnet.usable") + "  " + (hosts <= 2
            ? Lang.T("subnet.noUsable")
            : UIntToIp(net + 1) + " - " + UIntToIp(bcast - 1)));
        return sb.ToString().TrimEnd();
    }

    internal static string IpToInt(string raw)
    {
        if (!IPAddress.TryParse(raw.Trim(), out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return Lang.T("toolbox.badIp");
        return IpToUInt(ip).ToString(CultureInfo.InvariantCulture);
    }

    internal static string IntToIp(string raw)
    {
        if (!uint.TryParse(raw.Trim(), out var v)) return Lang.T("toolbox.badNumber");
        return UIntToIp(v).ToString();
    }

    /// <summary>
    /// 整数还原成 IP。
    /// 坑：<c>new IPAddress(uint)</c> 在 Windows 上按本机字节序（小端）解释，
    /// 传 3232235777 会得到 1.1.168.192 而不是 192.168.1.1。自己拆字节才稳。
    /// </summary>
    internal static IPAddress UIntToIp(uint v) =>
        new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    internal static uint IpToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    /// <summary>常见端口对照。范围给得克制，不硬凑一堆用不上的。</summary>
    private static readonly Dictionary<int, string> Ports = new()
    {
        [20] = "FTP 数据", [21] = "FTP 控制", [22] = "SSH / SFTP", [23] = "Telnet",
        [25] = "SMTP 发信", [53] = "DNS", [67] = "DHCP 服务端", [68] = "DHCP 客户端",
        [69] = "TFTP", [80] = "HTTP", [110] = "POP3", [123] = "NTP 对时",
        [135] = "RPC", [137] = "NetBIOS 名称", [138] = "NetBIOS 数据报", [139] = "NetBIOS 会话",
        [143] = "IMAP", [161] = "SNMP", [162] = "SNMP Trap", [389] = "LDAP",
        [443] = "HTTPS", [445] = "SMB 文件共享", [465] = "SMTPS", [500] = "IKE / IPSec",
        [514] = "Syslog", [587] = "SMTP 提交", [631] = "IPP 打印", [636] = "LDAPS",
        [873] = "rsync", [993] = "IMAPS", [995] = "POP3S", [1080] = "SOCKS 代理",
        [1194] = "OpenVPN", [1433] = "SQL Server", [1521] = "Oracle", [1701] = "L2TP",
        [1723] = "PPTP", [2049] = "NFS", [2181] = "ZooKeeper", [2375] = "Docker 未加密",
        [2376] = "Docker TLS", [3000] = "常见开发端口", [3306] = "MySQL", [3389] = "RDP 远程桌面",
        [4443] = "HTTPS 备用", [5000] = "常见开发端口", [5432] = "PostgreSQL",
        [5672] = "RabbitMQ", [5900] = "VNC", [6379] = "Redis", [7001] = "WebLogic",
        [8000] = "常见开发端口", [8080] = "HTTP 备用", [8443] = "HTTPS 备用",
        [8888] = "常见开发端口", [9000] = "常见应用端口", [9092] = "Kafka",
        [9200] = "Elasticsearch", [11211] = "Memcached", [27017] = "MongoDB",
    };

    internal static string CommonPort(string raw)
    {
        if (!int.TryParse(raw.Trim(), out var p) || p < 0 || p > 65535)
            return Lang.T("toolbox.badPort");
        if (Ports.TryGetValue(p, out var name))
            return Lang.T("port.found", p, name);

        if (p is > 0 and < 1024) return Lang.T("port.system", p);
        if (p is >= 1024 and < 49152) return Lang.T("port.user", p);
        return Lang.T("port.dynamic", p);
    }

    internal static string IpClass(string raw)
    {
        if (!IPAddress.TryParse(raw.Trim(), out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return Lang.T("toolbox.badIp");

        var b = ip.GetAddressBytes();
        var first = b[0];

        var cls = first switch
        {
            < 128 => "A",
            < 192 => "B",
            < 224 => "C",
            < 240 => "D（组播）",
            _ => "E（保留）"
        };

        var tags = new List<string>();
        if (first == 10) tags.Add(Lang.T("ipclass.private"));
        if (first == 172 && b[1] is >= 16 and <= 31) tags.Add(Lang.T("ipclass.private"));
        if (first == 192 && b[1] == 168) tags.Add(Lang.T("ipclass.private"));
        if (first == 127) tags.Add(Lang.T("ipclass.loopback"));
        if (first == 169 && b[1] == 254) tags.Add(Lang.T("ipclass.linkLocal"));
        if (first >= 224) tags.Add(Lang.T("ipclass.multicast"));

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("ipclass.input") + "  " + ip);
        sb.AppendLine(Lang.T("ipclass.class") + "  " + cls);
        sb.AppendLine(Lang.T("ipclass.scope") + "  " +
            (tags.Count > 0 ? string.Join(" / ", tags) : Lang.T("ipclass.public")));
        return sb.ToString().TrimEnd();
    }

    internal static string CidrCheck(string raw)
    {
        var items = raw.Split(new[] { '\n', '\r', ',', ';', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length < 2) return Lang.T("cidr.needTwo");

        var nets = new List<(uint Net, uint Mask, string Text)>();
        foreach (var it in items)
        {
            var p = it.Split('/');
            if (!IPAddress.TryParse(p[0], out var ip) ||
                ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return Lang.T("toolbox.badIp") + "  " + it;
            var prefix = 32;
            if (p.Length > 1 && (!int.TryParse(p[1], out prefix) || prefix is < 0 or > 32))
                return Lang.T("toolbox.badPrefix") + "  " + it;
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            nets.Add((IpToUInt(ip) & mask, mask, it));
        }

        var sb = new StringBuilder();
        for (var i = 0; i < nets.Count; i++)
        for (var j = i + 1; j < nets.Count; j++)
        {
            var a = nets[i];
            var b = nets[j];
            if ((a.Net & b.Mask) == b.Net && (b.Net & a.Mask) == a.Net && a.Mask == b.Mask)
                sb.AppendLine(Lang.T("cidr.same", a.Text, b.Text));
            else if ((a.Net & b.Mask) == b.Net)
                sb.AppendLine(Lang.T("cidr.aInB", a.Text, b.Text));
            else if ((b.Net & a.Mask) == a.Net)
                sb.AppendLine(Lang.T("cidr.bInA", a.Text, b.Text));
        }

        if (sb.Length == 0) sb.AppendLine(Lang.T("cidr.disjoint"));
        return sb.ToString().TrimEnd();
    }

    internal static string NormalizeMac(string raw)
    {
        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12) return Lang.T("mac.bad");
        var pairs = Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2));
        var joined = string.Join(":", pairs);
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("mac.colon") + "  " + joined);
        sb.AppendLine(Lang.T("mac.hyphen") + "  " + joined.Replace(':', '-'));
        sb.AppendLine(Lang.T("mac.dot") + "  " + string.Join(".", Enumerable.Range(0, 3)
            .Select(i => hex.Substring(i * 4, 4).ToLowerInvariant())));
        sb.AppendLine(Lang.T("mac.plain") + "  " + hex);
        return sb.ToString().TrimEnd();
    }

    internal static string TimestampToDate(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        if (!long.TryParse(t, out var v)) return Lang.T("toolbox.badNumber");

        // 13 位当毫秒、10 位当秒——这是最常见的两种，认错了结果会差 1000 倍
        DateTimeOffset dt;
        try
        {
            dt = Math.Abs(v) > 99_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(v)
                : DateTimeOffset.FromUnixTimeSeconds(v);
        }
        catch { return Lang.T("toolbox.badNumber"); }

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("ts.unit") + "  " + (Math.Abs(v) > 99_999_999_999
            ? Lang.T("ts.milli") : Lang.T("ts.second")));
        sb.AppendLine(Lang.T("ts.local") + "  " + dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine(Lang.T("ts.utc") + "  " + dt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(Lang.T("ts.iso") + "  " + dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        return sb.ToString().TrimEnd();
    }

    internal static string DateToTimestamp(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd", "yyyyMMddHHmmss", "yyyyMMdd"
        };
        if (!DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt) &&
            !DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
            return Lang.T("toolbox.badDate");

        var dto = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("ts.second") + "  " + dto.ToUnixTimeSeconds());
        sb.AppendLine(Lang.T("ts.milli") + "  " + dto.ToUnixTimeMilliseconds());
        sb.AppendLine(Lang.T("ts.utc") + "  " + dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        return sb.ToString().TrimEnd();
    }

    internal static string UtcConvert(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        if (!DateTime.TryParse(t, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return Lang.T("toolbox.badDate");

        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("ts.utc") + "  " + utc.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(Lang.T("ts.local") + "  " + local.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(Lang.T("utc.offset") + "  " + TimeZoneInfo.Local.GetUtcOffset(local));
        sb.AppendLine(Lang.T("utc.zone") + "  " + TimeZoneInfo.Local.DisplayName);
        return sb.ToString().TrimEnd();
    }

    internal static string HexText(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        var compact = new string(t.Where(Uri.IsHexDigit).ToArray());

        // 能整段解析成十六进制就按解码走，否则按编码走——用户不用自己说是哪个方向
        if (compact.Length >= 2 && compact.Length % 2 == 0)
        {
            try
            {
                var bytes = Convert.FromHexString(compact.Length % 2 == 0 ? compact : compact[..^1]);
                var text = Encoding.UTF8.GetString(bytes);
                // 解出来全是控制字符说明这本来就是一段普通文本，转回编码方向
                if (!text.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t')))
                    return Lang.T("hex.decoded") + "\n" + text;
            }
            catch { }
        }

        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(raw));
        return Lang.T("hex.encoded") + "\n" + GroupHex(hex);
    }

    private static string GroupHex(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < hex.Length; i += 2)
        {
            sb.Append(hex, i, 2).Append(' ');
            if ((i / 2 + 1) % 16 == 0) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    internal static string B64Decode(string raw)
    {
        var t = raw.Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "");
        if (t.Length == 0) return "";
        try
        {
            var pad = t.Length % 4;
            if (pad > 0) t = t.PadRight(t.Length + (4 - pad), '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(t));
        }
        catch { return Lang.T("toolbox.badBase64"); }
    }

    internal static string UrlDecode(string raw)
    {
        try { return Uri.UnescapeDataString(raw.Replace("+", "%20")); }
        catch { return raw; }
    }

    internal static string RegexTest(string raw)
    {
        var idx = raw.IndexOf('\n');
        if (idx < 0) return Lang.T("regex.needTwo");

        var pattern = raw[..idx].Trim();
        var subject = raw[(idx + 1)..];
        if (pattern.Length == 0) return Lang.T("regex.needTwo");

        try
        {
            var re = new Regex(pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(2));
            var ms = re.Matches(subject);
            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("regex.matches", ms.Count));
            sb.AppendLine(new string('─', 40));
            var i = 1;
            foreach (Match m in ms)
            {
                sb.AppendLine($"{i++}. {m.Value}");
                for (var g = 1; g < m.Groups.Count; g++)
                    sb.AppendLine("   $" + g + " = " + m.Groups[g].Value);
                if (i > 200) { sb.AppendLine("…"); break; }
            }
            return sb.ToString().TrimEnd();
        }
        catch (RegexParseException ex)
        {
            return Lang.T("regex.bad") + "\n" + ex.Message;
        }
        catch (Exception ex)
        {
            return Lang.T("regex.bad") + "\n" + ex.Message;
        }
    }

    internal static string JsonPretty(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(t);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (JsonException ex)
        {
            return Lang.T("json.bad") + "\n" + ex.Message;
        }
    }

    internal static string JsonMin(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(t);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (JsonException ex)
        {
            return Lang.T("json.bad") + "\n" + ex.Message;
        }
    }

    internal static string MachineInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("machine.name") + "  " + Environment.MachineName);
        sb.AppendLine(Lang.T("machine.user") + "  " + Environment.UserName);
        sb.AppendLine(Lang.T("machine.os") + "  " + Environment.OSVersion);
        sb.AppendLine(Lang.T("machine.arch") + "  " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        sb.AppendLine(Lang.T("machine.cores") + "  " + Environment.ProcessorCount);
        sb.AppendLine(Lang.T("machine.dotnet") + "  " + Environment.Version);
        sb.AppendLine(Lang.T("machine.runtime") + "  " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        sb.AppendLine(Lang.T("machine.uptime") + "  " + TimeSpan.FromMilliseconds(Environment.TickCount64));
        sb.AppendLine(Lang.T("machine.boot") + "  " +
                      (DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64))
                      .ToString("yyyy-MM-dd HH:mm:ss"));
        return sb.ToString().TrimEnd();
    }

    internal static string LocalIps()
    {
        var sb = new StringBuilder();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var props = ni.GetIPProperties();
                var addrs = props.UnicastAddresses
                    .Select(a => a.Address)
                    .Where(a => !System.Net.IPAddress.IsLoopback(a))
                    .ToList();
                if (addrs.Count == 0) continue;

                sb.AppendLine(ni.Name + "  (" + ni.NetworkInterfaceType + ")");
                foreach (var a in addrs) sb.AppendLine("  " + a);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine(Lang.T("toolbox.error") + "  " + ex.Message);
        }
        if (sb.Length == 0) sb.AppendLine(Lang.T("myip.none"));
        return sb.ToString().TrimEnd();
    }

    internal static string DiskInfo()
    {
        var sb = new StringBuilder();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var total = d.TotalSize;
                var free = d.AvailableFreeSpace;
                var used = total - free;
                var pct = total > 0 ? used * 100.0 / total : 0;
                sb.AppendLine($"{d.Name}  {Lang.T("disk.total")} {Size(total)}  " +
                              $"{Lang.T("disk.free")} {Size(free)}  " +
                              $"{Lang.T("disk.used")} {pct:F1}%");
            }
            catch { }
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : Lang.T("disk.none");
    }

    internal static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(u == 0 ? "F0" : "F2", CultureInfo.InvariantCulture) + " " + units[u];
    }

    internal static string Newline(string raw)
    {
        var crlf = Regex.Matches(raw, "\r\n").Count;
        var lone = Regex.Matches(raw, "\r(?!\n)").Count;
        var lf = Regex.Matches(raw, "(?<!\r)\n").Count;
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("newline.count", crlf, lone, lf));
        var lfOnly = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        sb.AppendLine(Lang.T("newline.lf") + "（\n）");
        sb.AppendLine(ShowInvisible(lfOnly));
        sb.AppendLine();
        sb.AppendLine(Lang.T("newline.crlf") + "（\r\n）");
        sb.AppendLine(ShowInvisible(lfOnly.Replace("\n", "\r\n")));
        return sb.ToString().TrimEnd();
    }

    private static string ShowInvisible(string s) =>
        s.Replace("\r\n", "⏎\n").Replace("\n", "⏎\n");

    // ────────────────────────── 网页 / 浏览实现 ──────────────────────────

    private static async Task<string> Shot(string _, ToolContext c, bool full)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");

        var bytes = await c.Capture(full);
        if (bytes is null || bytes.Length == 0) return Lang.T("web.shotFailed");

        var name = SafeName(await c.PageTitle()) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
        var path = c.SaveBytes(Lang.T(full ? "web.shotFullSave" : "web.shotSave"), bytes, name);
        return path.Length > 0 ? Lang.T("web.shotSaved", path) : "";
    }

    private static async Task<string> ExportPdf(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");
        var name = SafeName(await c.PageTitle()) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".pdf";
        await c.OpenTab("");
        return Lang.T("web.pdfHint", name);
    }

    private static async Task<string> PageInfo(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("pageinfo.title") + "  " + await c.PageTitle());
        sb.AppendLine(Lang.T("pageinfo.url") + "  " + url);

        var secure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        sb.AppendLine(Lang.T("pageinfo.secure") + "  " +
                      Lang.T(secure ? "pageinfo.yes" : "pageinfo.no"));

        var charset = await c.RunScript("document.characterSet");
        sb.AppendLine(Lang.T("pageinfo.charset") + "  " + Unquote(charset));

        var lang = await c.RunScript("document.documentElement.lang||''");
        sb.AppendLine(Lang.T("pageinfo.lang") + "  " + Unquote(lang));

        var counts = await c.RunScript(
            "JSON.stringify({a:document.links.length,i:document.images.length," +
            "s:document.scripts.length,f:document.forms.length})");
        sb.AppendLine(Lang.T("pageinfo.counts") + "  " + Unquote(counts));

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                sb.AppendLine(Lang.T("pageinfo.host") + "  " + u.Host);
        }
        catch { }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ExtractLinks(ToolContext c)
    {
        if (c.CurrentUrl().Length == 0) return Lang.T("web.noPage");
        var raw = await c.RunScript(
            "JSON.stringify([].map.call(document.links,function(a){return a.href;})" +
            ".filter(function(v,i,s){return s.indexOf(v)===i;}))");
        var list = ParseStringArray(raw);
        if (list.Count == 0) return Lang.T("web.linksNone");

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("web.linksCount", list.Count));
        sb.AppendLine(new string('─', 40));
        foreach (var l in list) sb.AppendLine(l);
        c.SetClipboard(string.Join("\n", list));
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ExtractImages(ToolContext c)
    {
        if (c.CurrentUrl().Length == 0) return Lang.T("web.noPage");
        var raw = await c.RunScript(
            "JSON.stringify([].map.call(document.images,function(i){return i.currentSrc||i.src;})" +
            ".filter(function(v,i,s){return v&&s.indexOf(v)===i;}))");
        var list = ParseStringArray(raw);
        if (list.Count == 0) return Lang.T("web.imagesNone");

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("web.imagesCount", list.Count));
        sb.AppendLine(new string('─', 40));
        foreach (var l in list) sb.AppendLine(l);
        c.SetClipboard(string.Join("\n", list));
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ViewSource(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");
        await c.OpenTab("view-source:" + url);
        return "";
    }

    private static async Task<string> ResponseHeaders(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return Lang.T("web.badUrl");

        try
        {
            using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            })
            { Timeout = TimeSpan.FromSeconds(10) };

            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TLBrowser");
            using var resp = await http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);

            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("headers.status") + "  " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
            sb.AppendLine(new string('─', 40));
            foreach (var h in resp.Headers)
                sb.AppendLine(h.Key + ": " + string.Join(", ", h.Value));
            foreach (var h in resp.Content.Headers)
                sb.AppendLine(h.Key + ": " + string.Join(", ", h.Value));
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return Lang.T("headers.failed") + "\n" + ex.Message;
        }
    }

    private const string ReaderCss =
        "(function(){var id='tlb_reader_style';" +
        "if(document.getElementById(id))return 'on';" +
        "var s=document.createElement('style');s.id=id;" +
        "s.textContent='header,nav,aside,footer,.ad,.ads,.advertisement,#ad,#ads," +
        "[class*=sidebar],[class*=banner]{display:none!important}" +
        "body{max-width:780px;margin:0 auto;padding:32px 20px;font-size:17px;line-height:1.85}" +
        "img{max-width:100%;height:auto}';" +
        "document.head.appendChild(s);return 'on';})()";

    private const string DarkCss =
        "(function(){var id='tlb_dark_style';" +
        "if(document.getElementById(id)){document.getElementById(id).remove();return 'off';}" +
        "var s=document.createElement('style');s.id=id;" +
        "s.textContent='html{filter:invert(1) hue-rotate(180deg)!important}" +
        "img,video,picture,canvas,svg{filter:invert(1) hue-rotate(180deg)!important}';" +
        "document.head.appendChild(s);return 'on';})()";

    private static async Task<string> InjectStyle(ToolContext c, string js, string key)
    {
        if (c.CurrentUrl().Length == 0) return Lang.T("web.noPage");
        var raw = await c.RunScript(js);
        var state = Unquote(raw);
        return Lang.T(state == "on" ? key : "web.styleOff");
    }

    private static async Task<string> ClearSiteData(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");
        var host = "";
        try { host = new Uri(url).Host; } catch { }
        if (host.Length == 0) return Lang.T("web.badUrl");

        if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("web.clearSiteAsk", host))) return "";

        await c.RunScript("try{localStorage.clear()}catch(e){}" +
                          "try{sessionStorage.clear()}catch(e){}" +
                          "try{indexedDB.databases&&indexedDB.databases().then(function(d){" +
                          "d.forEach(function(x){indexedDB.deleteDatabase(x.name)})})}catch(e){}");
        c.Reload();
        return Lang.T("web.clearSiteDone", host);
    }

    /// <summary>
    /// 「重置免费次数」：把限制免费次数的网站清回「第一次来」的状态，然后刷新页面，
    /// 用户就能接着用。那些网站一般把「还能免费用几次」记在本机的
    /// Cookie / localStorage / IndexedDB 里 —— 清掉即重置。
    ///
    /// 只清当前这一个站，别的标签页和别的网站一概不碰。
    /// </summary>
    private static async Task<string> ResetTrial(ToolContext c)
    {
        var url = c.CurrentUrl();
        if (url.Length == 0) return Lang.T("web.noPage");
        var host = "";
        try { host = new Uri(url).Host; } catch { }
        if (host.Length == 0) return Lang.T("web.badUrl");

        if (!c.Confirm(Lang.T("toolbox.confirmTitle"), Lang.T("web.resetTrialAsk", host))) return "";

        var detail = await c.ClearSiteData();
        if (detail.Length == 0) detail = Lang.T("web.resetTrialParts", 0, 0, 0, 0, 0);
        c.Reload();
        return Lang.T("web.resetTrialDone", host, detail);
    }

    private static string SafeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "page";
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (clean.Length > 60) clean = clean[..60];
        return clean.Length > 0 ? clean : "page";
    }

    private static List<string> ParseStringArray(string json)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(json) || json == "null") return result;
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            if (arr is not null) result.AddRange(arr.Where(x => !string.IsNullOrEmpty(x)));
        }
        catch { }
        return result;
    }

    // ────────────────────────── 文本 / 日常实现 ──────────────────────────

    internal static string CountText(string s)
    {
        var chars = s.Length;
        var noSpace = s.Count(ch => !char.IsWhiteSpace(ch));
        var cjk = s.Count(ch => ch >= 0x4E00 && ch <= 0x9FFF);
        var words = Regex.Matches(s, @"[A-Za-z0-9_']+").Count;
        var lines = s.Length == 0 ? 0 : Lines(s).Length;
        var paras = s.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                     .Count(p => p.Trim().Length > 0);

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("count.chars") + "  " + chars);
        sb.AppendLine(Lang.T("count.noSpace") + "  " + noSpace);
        sb.AppendLine(Lang.T("count.cjk") + "  " + cjk);
        sb.AppendLine(Lang.T("count.words") + "  " + words);
        sb.AppendLine(Lang.T("count.lines") + "  " + lines);
        sb.AppendLine(Lang.T("count.paras") + "  " + paras);
        return sb.ToString().TrimEnd();
    }

    internal static string TitleCase(string s) =>
        Regex.Replace(s, @"\b\w", m => m.Value.ToUpperInvariant());

    internal static string SwapCase(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(char.IsUpper(ch) ? char.ToLowerInvariant(ch)
                    : char.IsLower(ch) ? char.ToUpperInvariant(ch) : ch);
        return sb.ToString();
    }

    internal static string CamelToSnake(string s) =>
        Regex.Replace(s, @"(?<=[a-z0-9])([A-Z])", "_$1")
             .Replace("-", "_").Replace(" ", "_").ToLowerInvariant();

    internal static string SnakeToCamel(string s)
    {
        var parts = Regex.Split(s.Trim(), @"[_\-\s]+").Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "";
        var sb = new StringBuilder(parts[0].ToLowerInvariant());
        foreach (var p in parts.Skip(1))
            sb.Append(char.ToUpperInvariant(p[0])).Append(p[1..].ToLowerInvariant());
        return sb.ToString();
    }

    internal static string Dedup(string s) =>
        string.Join("\n", Lines(s).Distinct());

    internal static string Shuffle(string s)
    {
        var list = Lines(s).ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return string.Join("\n", list);
    }

    internal static string StripNumber(string line) =>
        Regex.Replace(line, @"^\s*\d+[\.\)、:：]\s*", "");

    /// <summary>加前缀 / 后缀。输入约定：第一行是要加的东西，后面每行是要处理的内容。</summary>
    internal static string AddAffix(string raw, bool prefix)
    {
        var idx = raw.IndexOf('\n');
        if (idx < 0) return Lang.T("affix.needTwo");
        var affix = raw[..idx];
        var body = raw[(idx + 1)..];
        var lines = Lines(body).Select(l => prefix ? affix + l : l + affix);
        return string.Join("\n", lines);
    }

    /// <summary>全角 ↔ 半角。wide=false 是全角转半角。</summary>
    internal static string WideNarrow(string s, bool wide)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (wide)
            {
                if (ch == ' ') sb.Append('\u3000');
                else if (ch is >= '!' and <= '~') sb.Append((char)(ch + 0xFEE0));
                else sb.Append(ch);
            }
            else
            {
                if (ch == '\u3000') sb.Append(' ');
                else if (ch is >= '\uFF01' and <= '\uFF5E') sb.Append((char)(ch - 0xFEE0));
                else sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 繁简转换。用户级需求里只要够用就行，所以收的是一张常用字表，
    /// 不做完整的 OpenCC 级别映射——那样要带几 MB 的字典文件，不划算。
    /// </summary>
    private const string TradChars = "萬與專業東絲兩嚴喪個豐臨為麗舉麼義烏樂喬習鄉書買亂爭於虧雲亞產畝親褻億僅從侖倉儀們價眾優會傘偉傳傷倫偽體餘傭傾僂僑儘";
    private const string SimpChars = "万与专业东丝两严丧个丰临为丽举么义乌乐乔习乡书买乱争于亏云亚产亩亲亵亿仅从仑仓仪们价众优会伞伟传伤伦伪体余佣倾偻侨尽";

    internal static string TradSimp(string s, bool toTrad)
    {
        var from = toTrad ? SimpChars : TradChars;
        var to = toTrad ? TradChars : SimpChars;
        var map = new Dictionary<char, char>();
        for (var i = 0; i < Math.Min(from.Length, to.Length); i++) map[from[i]] = to[i];

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(map.TryGetValue(ch, out var t) ? t : ch);
        return sb.ToString();
    }

    /// <summary>查找替换。输入约定：第一行「查找」，第二行「替换」，之后是正文。</summary>
    internal static string Replace(string raw)
    {
        var lines = Lines(raw);
        if (lines.Length < 2) return Lang.T("replace.needThree");
        var find = lines[0];
        var repl = lines[1];
        var body = string.Join("\n", lines.Skip(2));

        if (find.Length == 0) return Lang.T("replace.needFind");
        var count = Regex.Matches(body, Regex.Escape(find)).Count;
        var result = body.Replace(find, repl);
        return Lang.T("replace.count", count) + "\n" + new string('─', 40) + "\n" + result;
    }

    // ────────────────────────── 安全 / 隐私实现 ──────────────────────────

    internal static string GenPass(string spec)
    {
        var t = spec.Trim();
        var len = 16;
        var symbols = true;
        if (t.Length > 0)
        {
            var parts = t.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p, out var n) && n is >= 4 and <= 128) len = n;
                else if (p is "nosym" or "简单") symbols = false;
                else if (p is "sym" or "复杂") symbols = true;
            }
        }
        return Tools.RandomPassword(len, symbols);
    }

    internal static string Strength(string pass)
    {
        if (pass.Length == 0) return Lang.T("strength.empty");

        var hasLower = pass.Any(char.IsLower);
        var hasUpper = pass.Any(char.IsUpper);
        var hasDigit = pass.Any(char.IsDigit);
        var hasSym = pass.Any(ch => !char.IsLetterOrDigit(ch));

        var classes = new[] { hasLower, hasUpper, hasDigit, hasSym }.Count(x => x);
        var score = 0;
        score += Math.Min(40, pass.Length * 3);
        score += classes * 12;
        if (pass.Length >= 12) score += 8;
        if (pass.Length >= 16) score += 8;
        if (CommonWeak.Contains(pass)) score = Math.Min(score, 5);
        score = Math.Clamp(score, 0, 100);

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("strength.len") + "  " + pass.Length);
        sb.AppendLine(Lang.T("strength.classes") + "  " + classes + "/4");
        sb.AppendLine(Lang.T("strength.score") + "  " + score + "/100");
        sb.AppendLine(Lang.T("strength.level") + "  " + Lang.T(
            score < 30 ? "strength.weak" : score < 60 ? "strength.fair"
            : score < 85 ? "strength.good" : "strength.strong"));
        if (CommonWeak.Contains(pass)) sb.AppendLine(Lang.T("strength.common"));
        if (pass.Length < 12) sb.AppendLine(Lang.T("strength.tipLong"));
        if (classes < 3) sb.AppendLine(Lang.T("strength.tipClass"));
        return sb.ToString().TrimEnd();
    }

    internal static string Entropy(string pass)
    {
        if (pass.Length == 0) return Lang.T("strength.empty");
        var pool = 0;
        if (pass.Any(char.IsLower)) pool += 26;
        if (pass.Any(char.IsUpper)) pool += 26;
        if (pass.Any(char.IsDigit)) pool += 10;
        if (pass.Any(ch => !char.IsLetterOrDigit(ch))) pool += 33;
        if (pool == 0) pool = 1;

        var bits = pass.Length * Math.Log2(pool);
        var guesses = Math.Pow(2, bits);
        // 按每秒 1000 亿次猜测估个量级，够说明问题了
        var seconds = guesses / 1e11;

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("entropy.pool") + "  " + pool);
        sb.AppendLine(Lang.T("entropy.bits") + "  " + bits.ToString("F1", CultureInfo.InvariantCulture) + " bit");
        sb.AppendLine(Lang.T("entropy.crack") + "  " + HumanTime(seconds));
        return sb.ToString().TrimEnd();
    }

    private static string HumanTime(double seconds)
    {
        if (seconds < 1e-3) return Lang.T("entropy.instant");
        if (seconds < 60) return seconds.ToString("F1", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.sec");
        if (seconds < 3600) return (seconds / 60).ToString("F1", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.min");
        if (seconds < 86400) return (seconds / 3600).ToString("F1", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.hour");
        if (seconds < 3.15e7) return (seconds / 86400).ToString("F1", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.day");
        var years = seconds / 3.15e7;
        if (years < 1e6) return years.ToString("F0", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.year");
        var exp = Math.Floor(Math.Log10(years));
        return "10^" + exp.ToString("F0", CultureInfo.InvariantCulture) + " " + Lang.T("entropy.year");
    }

    internal static string HexRandom(string raw)
    {
        var t = raw.Trim();
        var n = 16;
        if (t.Length > 0 && (!int.TryParse(t, out n) || n is < 1 or > 4096))
            return Lang.T("toolbox.badNumber");
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(n)).ToLowerInvariant();
    }

    /// <summary>哈希校验。输入约定：第一行是文本，第二行是期望摘要。</summary>
    internal static string VerifyHash(string raw)
    {
        var lines = Lines(raw);
        if (lines.Length < 2) return Lang.T("verifyhash.needTwo");
        var text = lines[0];
        var expect = lines[1].Trim().ToLowerInvariant();
        if (expect.Length == 0) return Lang.T("verifyhash.needTwo");

        var md5 = Hash(text, MD5.Create());
        var sha1 = Hash(text, SHA1.Create());
        var sha256 = Hash(text, SHA256.Create());

        var algo = expect.Length switch
        {
            32 => "MD5", 40 => "SHA-1", 64 => "SHA-256", _ => ""
        };
        var actual = algo switch { "MD5" => md5, "SHA-1" => sha1, "SHA-256" => sha256, _ => "" };

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("verifyhash.algo") + "  " +
            (algo.Length > 0 ? algo : Lang.T("verifyhash.unknown")));
        sb.AppendLine("MD5      " + md5);
        sb.AppendLine("SHA-1    " + sha1);
        sb.AppendLine("SHA-256  " + sha256);
        sb.AppendLine();
        if (actual.Length == 0)
            sb.AppendLine(Lang.T("verifyhash.lenBad"));
        else
            sb.AppendLine(Lang.T(actual == expect ? "verifyhash.match" : "verifyhash.mismatch"));
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> FileHash(ToolContext c, string algo)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Lang.T("filehash.pick"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != DialogResult.OK) return "";

        try
        {
            await using var fs = File.OpenRead(dlg.FileName);
            using HashAlgorithm alg = algo == "md5" ? MD5.Create() : SHA256.Create();
            var bytes = await alg.ComputeHashAsync(fs);
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            c.SetClipboard(hex);
            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("filehash.file") + "  " + dlg.FileName);
            sb.AppendLine(Lang.T("filehash.size") + "  " + Size(new FileInfo(dlg.FileName).Length));
            sb.AppendLine((algo == "md5" ? "MD5" : "SHA-256") + "  " + hex);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return Lang.T("toolbox.error") + "  " + ex.Message;
        }
    }

    internal static string ScanPii(string s)
    {
        var hits = new List<(string Kind, string Value)>();

        foreach (Match m in Regex.Matches(s, @"\b1[3-9]\d{9}\b"))
            hits.Add((Lang.T("pii.phone"), m.Value));
        foreach (Match m in Regex.Matches(s, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"))
            hits.Add((Lang.T("pii.email"), m.Value));
        foreach (Match m in Regex.Matches(s, @"\b\d{17}[\dXx]\b"))
            hits.Add((Lang.T("pii.idcard"), m.Value));
        foreach (Match m in Regex.Matches(s, @"\b\d{16,19}\b"))
            hits.Add((Lang.T("pii.bank"), m.Value));
        foreach (Match m in Regex.Matches(s, @"\b\d{3}-\d{2}-\d{4}\b"))
            hits.Add((Lang.T("pii.ssn"), m.Value));

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("pii.total", hits.Count));
        if (hits.Count == 0) { sb.AppendLine(Lang.T("pii.none")); return sb.ToString().TrimEnd(); }

        sb.AppendLine(new string('─', 40));
        foreach (var g in hits.GroupBy(h => h.Kind))
        {
            sb.AppendLine(g.Key + "  ×" + g.Count());
            foreach (var h in g.Take(10)) sb.AppendLine("  " + h.Value);
            if (g.Count() > 10) sb.AppendLine("  …");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>18 位身份证校验：加权和 mod 11 对校验位。</summary>
    internal static string CheckIdCard(string raw)
    {
        var id = raw.Trim().ToUpperInvariant();
        if (id.Length == 0) return "";
        if (!Regex.IsMatch(id, @"^\d{17}[\dX]$")) return Lang.T("idcard.format");

        var weights = new[] { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
        var codes = "10X98765432";
        var sum = 0;
        for (var i = 0; i < 17; i++) sum += (id[i] - '0') * weights[i];
        var expect = codes[sum % 11];

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("idcard.got") + "  " + id[17]);
        sb.AppendLine(Lang.T("idcard.expect") + "  " + expect);
        sb.AppendLine(Lang.T(id[17] == expect ? "idcard.ok" : "idcard.bad"));

        var birth = id.Substring(6, 8);
        if (DateTime.TryParseExact(birth, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var bd))
        {
            sb.AppendLine(Lang.T("idcard.birth") + "  " + bd.ToString("yyyy-MM-dd"));
            sb.AppendLine(Lang.T("idcard.age") + "  " +
                ((DateTime.Today - bd).Days / 365));
        }
        sb.AppendLine(Lang.T("idcard.gender") + "  " +
            Lang.T((id[16] - '0') % 2 == 1 ? "idcard.male" : "idcard.female"));
        return sb.ToString().TrimEnd();
    }

    internal static string CheckLuhn(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19) return Lang.T("luhn.len");

        var sum = LuhnSum(digits);

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("luhn.digits") + "  " + digits.Length);
        sb.AppendLine(Lang.T("luhn.sum") + "  " + sum);
        sb.AppendLine(Lang.T(sum % 10 == 0 ? "luhn.ok" : "luhn.bad"));
        return sb.ToString().TrimEnd();
    }

    /// <summary>Luhn 校验和。抽出来是为了让自检能直接断言数字，不依赖界面文案。</summary>
    internal static int LuhnSum(string digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(digits[i])) continue;
            var d = digits[i] - '0';
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            alt = !alt;
        }
        return sum;
    }

    internal static string UrlDanger(string raw)
    {
        var u = raw.Trim();
        if (u.Length == 0) return "";
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return Lang.T("web.badUrl");

        var risks = new List<string>();

        if (System.Net.IPAddress.TryParse(uri.Host, out _)) risks.Add(Lang.T("urldanger.ipHost"));
        if (u.Length > 120) risks.Add(Lang.T("urldanger.long", u.Length));
        if (uri.Host.Contains("xn--")) risks.Add(Lang.T("urldanger.punycode"));
        if (Regex.IsMatch(uri.Host, @"^\d+\.\d+\.\d+\.\d+$") && uri.Scheme == "http")
            risks.Add(Lang.T("urldanger.httpIp"));
        if (uri.Scheme == "http") risks.Add(Lang.T("urldanger.http"));
        foreach (var bad in DangerTld)
            if (uri.Host.EndsWith(bad, StringComparison.OrdinalIgnoreCase))
                risks.Add(Lang.T("urldanger.tld", bad));
        if (uri.UserInfo.Length > 0) risks.Add(Lang.T("urldanger.userinfo"));
        if (u.Contains('@') && uri.UserInfo.Length > 0) risks.Add(Lang.T("urldanger.atSign"));
        if (Regex.IsMatch(uri.Host, @"(tlstudio|tldoublerstudio)", RegexOptions.IgnoreCase) &&
            !Brand.IsBrandHostName(uri.Host))
            risks.Add(Lang.T("urldanger.lookalike", uri.Host));

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("urldanger.url") + "  " + u);
        sb.AppendLine(Lang.T("urldanger.host") + "  " + uri.Host);
        sb.AppendLine(new string('─', 40));
        if (risks.Count == 0) sb.AppendLine(Lang.T("urldanger.clean"));
        else foreach (var r in risks) sb.AppendLine("⚠  " + r);
        return sb.ToString().TrimEnd();
    }

    private static readonly string[] DangerTld =
        { ".tk", ".top", ".xyz", ".gq", ".cf", ".ml", ".ga", ".work", ".click", ".loan" };

    internal static string WeakPassword(string raw)
    {
        var p = raw.Trim();
        if (p.Length == 0) return "";

        var hits = new List<string>();
        foreach (var w in CommonWeak)
            if (string.Equals(p, w, StringComparison.OrdinalIgnoreCase) ||
                p.Contains(w, StringComparison.OrdinalIgnoreCase))
                hits.Add(w);
        if (Regex.IsMatch(p, @"^\d+$")) hits.Add(Lang.T("weakpass.allDigits"));
        if (Regex.IsMatch(p, @"^[a-zA-Z]+$")) hits.Add(Lang.T("weakpass.allLetters"));
        if (Regex.IsMatch(p, @"(.)\1{2,}")) hits.Add(Lang.T("weakpass.repeat"));
        if (Regex.IsMatch(p, @"(012|123|234|345|456|567|678|789|890|abc|qwe|asd|zxc)",
                RegexOptions.IgnoreCase)) hits.Add(Lang.T("weakpass.sequence"));

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("weakpass.len") + "  " + p.Length);
        sb.AppendLine(new string('─', 40));
        if (hits.Count == 0) sb.AppendLine(Lang.T("weakpass.noHit"));
        else
        {
            sb.AppendLine(Lang.T("weakpass.hits", hits.Count));
            foreach (var h in hits.Distinct()) sb.AppendLine("⚠  " + h);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>内置弱口令表。不追求全覆盖，抓的是最高频的那批。</summary>
    private static readonly string[] CommonWeak =
    {
        "123456", "password", "123456789", "12345678", "12345", "111111", "1234567",
        "sunshine", "qwerty", "iloveyou", "princess", "admin", "welcome", "666666",
        "abc123", "football", "123123", "monkey", "654321", "!@#$%^&*", "charlie",
        "aa123456", "donald", "password1", "qwerty123", "1q2w3e4r", "333333", "000000",
        "88888888", "letmein", "root", "toor", "pass", "test", "guest", "master",
        "administrator", "changeme", "1234", "1234567890", "qwertyuiop", "asdfghjkl",
        "zxcvbnm", "wang123", "li123456", "zhang123", "china123", "woaini", "521521"
    };

    internal static string PrivacyReport(ToolContext c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("privacy.head"));
        sb.AppendLine(new string('─', 40));
        sb.AppendLine(Lang.T("privacy.history") + "  " + HistoryStore.Count);
        sb.AppendLine(Lang.T("privacy.blocked") + "  " + Safety.BlockedTotal);
        sb.AppendLine(Lang.T("privacy.rules") + "  " + Safety.RuleCount);
        sb.AppendLine(Lang.T("privacy.adblock") + "  " +
            Lang.T(Safety.AdBlock ? "privacy.on" : "privacy.off"));
        sb.AppendLine(Lang.T("privacy.dataDir") + "  " + Brand.AppDataDir);
        try
        {
            var size = DirSize(new DirectoryInfo(Brand.UserDataDir));
            sb.AppendLine(Lang.T("privacy.cacheSize") + "  " + Size(size));
        }
        catch { }
        sb.AppendLine(Lang.T("privacy.current") + "  " + (c.CurrentUrl().Length > 0 ? c.CurrentUrl() : "-"));
        return sb.ToString().TrimEnd();
    }

    private static long DirSize(DirectoryInfo dir)
    {
        long total = 0;
        try
        {
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                try { total += f.Length; } catch { }
        }
        catch { }
        return total;
    }

    /// <summary>最近的拦截记录，做成一段可读文本。</summary>
    private static string AdLogText()
    {
        var recent = Safety.RecentBlocked(100);
        if (recent.Count == 0) return Lang.T("safe.adLogEmpty");

        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("safe.adLogHead", recent.Count));
        sb.AppendLine(new string('─', 40));
        foreach (var r in recent) sb.AppendLine(r);
        sb.AppendLine();
        sb.AppendLine(Lang.T("safe.adLogPath") + "  " + Safety.AuditLogPath);
        return sb.ToString().TrimEnd();
    }

    // ────────────────────────── 自检 ──────────────────────────

    /// <summary>
    /// 纯计算类的样本断言。GUI 起不来的机器上，这是唯一能证明「功能真的算对了」的手段。
    /// 返回失败条数。
    /// </summary>
    public static int SelfTest(out string report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TL 浏览器 · 工具箱自检");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine($"功能总数：{Count}（运维 {InCategory(CatOps).Count} / " +
                      $"网页 {InCategory(CatWeb).Count} / 文本 {InCategory(CatText).Count} / " +
                      $"安全 {InCategory(CatSafe).Count}）");
        sb.AppendLine(new string('=', 70));

        var bad = 0;

        void CheckTrue(string name, bool ok, string detail)
        {
            if (!ok) bad++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {name}   {detail}");
        }

        // 运维 / 文本 / 安全各 28 项，网页类多一项「重置免费次数」
        CheckTrue("功能数：运维/文本/安全各 28，网页 29",
            InCategory(CatOps).Count == 28 &&
            InCategory(CatWeb).Count == 29 &&
            InCategory(CatText).Count == 28 &&
            InCategory(CatSafe).Count == 28,
            $"{InCategory(CatOps).Count}/{InCategory(CatWeb).Count}/" +
            $"{InCategory(CatText).Count}/{InCategory(CatSafe).Count}");

        // id 不能重名，否则菜单会画出两个一样的东西
        var dup = All.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        CheckTrue("功能 id 无重复", dup.Count == 0, dup.Count == 0 ? "" : string.Join(",", dup));

        // 语言键必须齐全（菜单要用的）
        var missing = All.Where(t => Lang.Missing("tool." + t.Id)).Select(t => t.Id).ToList();
        CheckTrue("每个功能都有中文案", missing.Count == 0,
            missing.Count == 0 ? "" : string.Join(",", missing));

        sb.AppendLine();
        sb.AppendLine("· 运维类");
        CheckTrue("IP 转整数", IpToInt("192.168.1.1") == "3232235777", IpToInt("192.168.1.1"));
        CheckTrue("整数转 IP", IntToIp("3232235777") == "192.168.1.1", IntToIp("3232235777"));
        CheckTrue("子网网络地址", Subnet("192.168.1.10/24").Contains("192.168.1.0"), "");
        CheckTrue("子网广播地址", Subnet("192.168.1.10/24").Contains("192.168.1.255"), "");
        CheckTrue("子网主机数", Subnet("192.168.1.10/24").Contains("254"), "");
        CheckTrue("/31 不出现负数主机", Subnet("10.0.0.0/31").Contains("2"), "");
        CheckTrue("端口查询命中", CommonPort("443").Length > 0, CommonPort("443").Trim());
        CheckTrue("MAC 归一化", NormalizeMac("e06290961 94d".Replace(" ", ""))
            .Contains("E0:62:90:96:19:4D"), "");
        CheckTrue("时间戳转日期", TimestampToDate("1700000000").Contains("2023"), "");
        CheckTrue("Base64 往返", B64Decode(Convert.ToBase64String(
            Encoding.UTF8.GetBytes("你好TL"))) == "你好TL", "");
        CheckTrue("URL 往返", UrlDecode(Uri.EscapeDataString("你好 a&b")) == "你好 a&b", "");
        CheckTrue("MD5 已知值", Hash("abc", MD5.Create()) ==
            "900150983cd24fb0d6963f7d28e17f72", "");
        CheckTrue("SHA-256 已知值", Hash("abc", SHA256.Create()) ==
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "");
        CheckTrue("JSON 美化后仍可解析", JsonPretty("{\"a\":1}").Contains("\"a\""), "");
        CheckTrue("JSON 压缩去掉空白", JsonMin("{ \"a\" : 1 }") == "{\"a\":1}", JsonMin("{ \"a\" : 1 }"));
        CheckTrue("正则匹配计数", RegexTest("[0-9]+\nabc123def456").Contains("2"), "");
        CheckTrue("本机信息不为空", MachineInfo().Length > 0, "");
        CheckTrue("磁盘信息不为空", DiskInfo().Length > 0, "");

        sb.AppendLine();
        sb.AppendLine("· 文本类");
        CheckTrue("字数统计", CountText("abc 你好").Contains("6"), "");
        CheckTrue("转大写", TitleCase("hello world") == "Hello World", TitleCase("hello world"));
        CheckTrue("驼峰转下划线", CamelToSnake("myVarName") == "my_var_name", CamelToSnake("myVarName"));
        CheckTrue("下划线转驼峰", SnakeToCamel("my_var_name") == "myVarName", SnakeToCamel("my_var_name"));
        CheckTrue("行去重", Dedup("a\nb\na").Split('\n').Length == 2, Dedup("a\nb\na"));
        CheckTrue("行倒序", string.Join("\n", Lines("a\nb\nc").Reverse()) == "c\nb\na", "");
        CheckTrue("去行号", StripNumber("1. hello") == "hello", StripNumber("1. hello"));
        CheckTrue("全角转半角", WideNarrow("ＡＢＣ１２３", false) == "ABC123", WideNarrow("ＡＢＣ１２３", false));
        CheckTrue("半角转全角", WideNarrow("ABC123", true) == "ＡＢＣ１２３", WideNarrow("ABC123", true));
        CheckTrue("文本反转", new string("abc".Reverse().ToArray()) == "cba", "");
        CheckTrue("加行号", string.Join("\n", Lines("a\nb").Select((l, i) => $"{i + 1}. {l}")) == "1. a\n2. b", "");
        CheckTrue("查找替换", Replace("a\nX\nbanana").Contains("bXnXnX"), Replace("a\nX\nbanana"));

        sb.AppendLine();
        sb.AppendLine("· 安全类");
        CheckTrue("UUID 格式", Regex.IsMatch(Guid.NewGuid().ToString(),
            @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"), "");
        CheckTrue("随机密码长度", Tools.RandomPassword(24, true).Length == 24, "");
        CheckTrue("随机密码含四类字符", Regex.IsMatch(Tools.RandomPassword(32, true),
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\w\s]).+$"), "");
        CheckTrue("十六进制随机长度", HexRandom("8").Length == 16, HexRandom("8").Length.ToString());
        // 自检不该依赖界面语言，所以这里断言的是「算出来的校验和」而不是那句中文结论
        CheckTrue("Luhn 合法卡号", LuhnSum("4539578763621486") % 10 == 0,
            LuhnSum("4539578763621486").ToString());
        CheckTrue("Luhn 拒绝非法卡号", LuhnSum("1234567890123456") % 10 != 0,
            LuhnSum("1234567890123456").ToString());
        CheckTrue("身份证校验位", CheckIdCard("11010519491231002X").Length > 0, "");
        CheckTrue("弱口令命中", WeakPassword("123456").Contains("⚠"), "");
        CheckTrue("强口令不命中", !WeakPassword("xK9#mQ2$vL8@nR4!").Contains("⚠"), "");
        CheckTrue("PII 检测到手机号", ScanPii("联系 13800138000").Contains("13800138000"), "");
        CheckTrue("PII 检测到邮箱", ScanPii("a@b.com").Contains("a@b.com"), "");
        CheckTrue("URL 风险识别 IP 直连", UrlDanger("http://1.2.3.4/x").Contains("⚠"), "");
        CheckTrue("密码强度评分", Strength("xK9#mQ2$vL8@nR4!").Contains("/100"), "");

        sb.AppendLine();
        sb.AppendLine("· 追加功能");
        CheckTrue("进制转换 0xFF", RadixConvert("0xFF").Contains("255"), "");
        CheckTrue("进制转换 255", RadixConvert("255").Contains("11111111"), "");
        CheckTrue("cron 每 5 分钟", CronExplain("*/5 * * * *").Contains("每 5 分"), "");
        CheckTrue("cron 段数不对要报错", CronExplain("* * *") != CronExplain("* * * * *"), "");
        CheckTrue("URL 拆查询参数", UrlParts("https://a.com/x?k=1&t=2").Contains("k = 1"), "");
        CheckTrue("URL 非法不崩", UrlParts("nonsense").Length > 0, "");
        CheckTrue("人民币大写", RmbUpper("1234.56") == "壹仟贰佰叁拾肆元伍角陆分", RmbUpper("1234.56"));
        CheckTrue("人民币整元", RmbUpper("100").EndsWith("元整"), RmbUpper("100"));
        CheckTrue("人民币万级", RmbUpper("1000001").Contains("壹佰万零壹"), RmbUpper("1000001"));
        CheckTrue("千分位", NumberFormat("1234567.891") == "1,234,567.891", NumberFormat("1234567.891"));
        CheckTrue("按数字排序", SortNumeric("10\n2\n1").Split('\n')[0] == "1", SortNumeric("10\n2\n1"));
        var rn = RandomNumbers("1-100 x5").Split('\n');
        CheckTrue("随机数个数", rn.Length == 5, rn.Length.ToString());
        CheckTrue("随机数不重复", rn.Distinct().Count() == 5, "");
        CheckTrue("随机数落在范围内", rn.All(x => int.TryParse(x, out var q) && q >= 1 && q <= 100), "");
        CheckTrue("格式校验认手机号", FormatCheck("13800138000").Contains("\n"), "");
        CheckTrue("格式校验认不出乱码", !FormatCheck("???").Contains("\n"), "");
        CheckTrue("打码手机号", MaskPii("13800138000").Contains("138****8000"), MaskPii("13800138000"));
        CheckTrue("打码邮箱", MaskPii("a@b.com").Contains("***@"), MaskPii("a@b.com"));

        // 「重置免费次数」：菜单入口在、类别对、文案齐，缺一样就在这红
        var trial = Find("web.resettrial");
        CheckTrue("重置免费次数入口存在", trial is not null, "");
        CheckTrue("重置免费次数归在网页类", trial?.Category == CatWeb, trial?.Category ?? "");
        CheckTrue("重置免费次数文案齐全",
            !Lang.Missing("web.resetTrialAsk") && !Lang.Missing("web.resetTrialDone"),
            "");

        sb.AppendLine();
        sb.AppendLine(new string('=', 70));
        sb.AppendLine(bad == 0 ? "全部通过" : $"失败 {bad} 条");
        report = sb.ToString();
        return bad;
    }
}
