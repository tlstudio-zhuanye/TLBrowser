using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TLBrowser;

internal enum GuardKind { StartupArg, Shortcut, HomeFile, System }

/// <summary>一条守护记录：要么是「已拦下并修好」，要么是「只是看见，报告给你」。</summary>
internal sealed class GuardEvent
{
    public DateTime Time { get; init; } = DateTime.Now;
    public GuardKind Kind { get; init; }
    public string Item { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>true = 已经被本程序自动处理掉了；false = 仅报告，没动它。</summary>
    public bool Handled { get; init; }

    /// <summary>系统检查用：true 表示「确实需要处理」，false 只是告知现状。</summary>
    public bool Attention { get; init; }

    /// <summary>系统检查用的短分类（主页项 / 搜索项 / 锁定 / 启动项 / 360）。</summary>
    public string Category { get; init; } = "";

    /// <summary>列表里放不下的补充说明，只在悬停提示里显示。</summary>
    public string Extra { get; init; } = "";

    public string KindText => Kind switch
    {
        GuardKind.StartupArg => "启动参数",
        GuardKind.Shortcut => "快捷方式",
        GuardKind.HomeFile => "首页文件",
        _ => "系统检查"
    };

    public string StatusText => Handled ? "已处理" : "仅报告";

    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>
/// 主页守护。
///
/// 本程序把首页和搜索引擎**写死在代码里**（首页是内嵌资源，搜索地址是常量），
/// 不像常规浏览器那样存在可被外部改写的配置文件，所以外部程序改不动它。
/// 真正能被外部动手的只有两处：
///   1. 桌面/开始菜单快捷方式的启动参数 —— 塞个网址进去就能顶掉起始页；
///   2. 落盘到 LOCALAPPDATA 的首页 index.html。
/// 这两处每次启动都自检并还原，改动会被记录到 guard.log。
/// 其余系统层面的东西（IE 主页项、主页锁定策略、别的浏览器快捷方式）
/// **只读取并报告**；要动手必须用户在面板上明确确认，并且先备份。
/// </summary>
internal static class HomeGuard
{
    /// <summary>本程序认识的启动开关，除此之外的参数一律不算数。</summary>
    private static readonly string[] KnownSwitches = { "--dual", "--nosplash", "--guard" };

    /// <summary>本次运行的记录（GuardForm 直接读它来展示）。</summary>
    public static List<GuardEvent> Session { get; } = new();

    public static int HandledCount => Session.Count(e => e.Handled);
    public static int ReportCount => Session.Count(e => !e.Handled);

    /// <summary>系统层面的只读检查结果（和 Session 分开存，避免混进「已处理」计数）。</summary>
    public static List<GuardEvent> SystemChecks { get; } = new();

    /// <summary>系统检查里「确实需要处理」的条数，用来决定盾牌要不要变告警色。</summary>
    public static int AttentionCount => SystemChecks.Count(e => e.Attention);

    // ────────────────────────────── 记录 ──────────────────────────────

    public static void Log(GuardKind kind, string item, string detail, bool handled)
    {
        var e = new GuardEvent { Kind = kind, Item = item, Detail = detail, Handled = handled };
        Session.Add(e);
        try
        {
            File.AppendAllText(Brand.GuardLogPath,
                $"[{e.Time:yyyy-MM-dd HH:mm:ss}] {e.KindText} | {e.Item} | {e.Detail} | {e.StatusText}\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void Note(string category, string item, string detail, bool attention, string extra = "") =>
        SystemChecks.Add(new GuardEvent
        {
            Kind = GuardKind.System,
            Category = category,
            Item = item,
            Detail = detail,
            Attention = attention,
            Extra = extra
        });

    // ────────────────────────────── 1. 启动参数白名单 ──────────────────────────────

    /// <summary>
    /// 启动参数只认自己人的。
    /// 360 之类改主页最常见的手法就是往快捷方式里追加一个网址参数，
    /// 常规浏览器会照单全收当成启动页——这里白名单之外一律丢弃并记录。
    /// </summary>
    public static string? GateStartUrl(string[] args, out bool dual, out bool noSplash, out bool openGuard)
    {
        dual = false;
        noSplash = false;
        openGuard = false;
        string? url = null;

        foreach (var raw in args)
        {
            var a = raw.Trim();
            if (a.Length == 0) continue;

            if (a.StartsWith('-'))
            {
                if (a.Equals("--dual", StringComparison.OrdinalIgnoreCase)) { dual = true; continue; }
                if (a.Equals("--nosplash", StringComparison.OrdinalIgnoreCase)) { noSplash = true; continue; }
                if (a.Equals("--guard", StringComparison.OrdinalIgnoreCase)) { openGuard = true; continue; }
                Log(GuardKind.StartupArg, a, "不认识的启动开关，已忽略", true);
                continue;
            }

            if (Brand.IsBrandUrl(a))
            {
                url ??= a;
                continue;
            }

            Log(GuardKind.StartupArg, Shorten(a),
                LooksLikeUrl(a) ? "外部程序试图把它当成起始页，已拦截，改回本站首页" : "不是本站地址，已忽略",
                true);
        }

        return url;
    }

    // ────────────────────────────── 2. 快捷方式自愈 ──────────────────────────────

    /// <summary>
    /// 检查我们自己的快捷方式：目标有没有被换掉、参数里有没有被塞网址、图标有没有被改。
    /// 三种情况都直接改回来。别人的快捷方式只报告不碰。
    /// </summary>
    public static void HealShortcuts()
    {
        var exe = Brand.AppExePath;
        if (string.IsNullOrEmpty(exe)) return;

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;                 // 拿不到 COM 就安静跳过，不影响启动

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            var reportedForeign = 0;
            foreach (var (dir, deep) in ShortcutDirs())
            {
                if (!Directory.Exists(dir)) continue;

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir, "*.lnk",
                        deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }
                catch { continue; }

                foreach (var lnk in files)
                {
                    // 开始菜单有几百个 lnk，逐个走 COM 太慢；深目录只挑名字像我们的
                    if (deep && !NameLooksOurs(lnk)) continue;
                    try
                    {
                        if (CheckShortcut(shell, lnk, exe) && reportedForeign < 6) reportedForeign++;
                    }
                    catch { }
                }
            }
        }
        catch { }
        finally
        {
            if (shell is not null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
        }
    }

    /// <summary>返回 true 表示这是一条「别人的快捷方式被追加了网址」的报告。</summary>
    private static bool CheckShortcut(object shell, string lnk, string exe)
    {
        var scType = shell.GetType();
        object? sc = null;
        try
        {
            sc = scType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                null, shell, new object[] { lnk });
            if (sc is null) return false;

            var target = Str(sc, "TargetPath");
            var args = Str(sc, "Arguments");
            var icon = Str(sc, "IconLocation");
            var name = Path.GetFileName(lnk);

            var targetsUs = string.Equals(target, exe, StringComparison.OrdinalIgnoreCase);
            var nameIsOurs = NameLooksOurs(lnk);

            // (a) 不是我们的快捷方式：只看看有没有被塞网址，不碰它
            if (!targetsUs && !nameIsOurs)
            {
                if (!LooksLikeUrl(args)) return false;
                Log(GuardKind.System, name,
                    $"别的浏览器快捷方式被追加了网址 {Shorten(args)}（未改动，仅供参考）", false);
                return true;
            }

            // (b) 名字是我们的、目标却不是 → 被改指到别处了
            if (!targetsUs)
            {
                Log(GuardKind.Shortcut, name, $"目标被改成了 {Shorten(target)}，已还原", true);
                Repair(sc, lnk, exe, args);
                return false;
            }

            // (c) 目标正确 → 查参数和图标
            var stray = StrayArgs(args);
            var iconBad = IconIsForeign(icon, exe);
            if (stray.Count == 0 && !iconBad) return false;

            var why = new List<string>();
            if (stray.Count > 0) why.Add("被追加了 " + string.Join(" ", stray));
            if (iconBad) why.Add("图标被改成了 " + Shorten(icon));
            Log(GuardKind.Shortcut, name, string.Join("；", why) + " —— 已清理", true);
            Repair(sc, lnk, exe, args);
            return false;
        }
        finally
        {
            if (sc is not null) { try { Marshal.FinalReleaseComObject(sc); } catch { } }
        }
    }

    private static void Repair(object shortcut, string lnk, string exe, string oldArgs)
    {
        var t = shortcut.GetType();
        void Set(string prop, string value) =>
            t.InvokeMember(prop, System.Reflection.BindingFlags.SetProperty, null, shortcut,
                new object[] { value });
        object? Call(string method) =>
            t.InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

        Set("TargetPath", exe);
        Set("WorkingDirectory", Path.GetDirectoryName(exe) ?? "");
        // 关键：只留下我们自己认识的开关，塞进来的网址在这里被抹掉
        Set("Arguments", string.Join(" ", KnownSwitches
            .Where(k => oldArgs.Contains(k, StringComparison.OrdinalIgnoreCase))));
        Set("IconLocation", exe + ",0");
        Call("Save");
    }

    private static IEnumerable<(string Dir, bool Deep)> ShortcutDirs()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
        yield return (Path.Combine(local, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), false);
        yield return (Path.Combine(roaming, @"Microsoft\Windows\Start Menu\Programs"), true);
        yield return (Path.Combine(programData, @"Microsoft\Windows\Start Menu\Programs"), true);
    }

    private static bool NameLooksOurs(string lnk)
    {
        var n = Path.GetFileNameWithoutExtension(lnk);
        return n.Contains("TLBrowser", StringComparison.OrdinalIgnoreCase)
            || n.Contains("TL ", StringComparison.OrdinalIgnoreCase)
            || n.Contains("浏览器", StringComparison.Ordinal)
            || n.Contains("tlstudio", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>取出参数里所有不属于白名单的项（网址会被完整列出来）。</summary>
    private static List<string> StrayArgs(string args)
    {
        var stray = new List<string>();
        if (string.IsNullOrWhiteSpace(args)) return stray;

        foreach (var part in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (KnownSwitches.Any(k => part.Equals(k, StringComparison.OrdinalIgnoreCase))) continue;
            stray.Add(Shorten(part));
        }
        return stray;
    }

    private static bool IconIsForeign(string icon, string exe)
    {
        if (string.IsNullOrWhiteSpace(icon)) return false;
        var path = icon.Split(',')[0].Trim();
        if (path.Length == 0) return false;
        return !string.Equals(path, exe, StringComparison.OrdinalIgnoreCase);
    }

    // ────────────────────────────── 3. 首页文件完整性 ──────────────────────────────

    /// <summary>
    /// 首页是内嵌资源，本该每次启动覆盖写盘。这里在覆盖**之前**先比对一次，
    /// 好在被外部改过的时候能记录下「谁动过、动了什么」，然后还原。
    /// </summary>
    public static void VerifyHomeFile()
    {
        try
        {
            var expected = Brand.ExpectedHomeHtml;
            if (expected.Length == 0) return;

            var path = Brand.HomeFilePath;
            if (!File.Exists(path))
            {
                File.WriteAllText(path, expected, new UTF8Encoding(false));
                Log(GuardKind.HomeFile, "index.html", "首页文件缺失，已按内嵌资源重建", true);
                return;
            }

            var onDisk = File.ReadAllText(path, Encoding.UTF8);
            if (string.Equals(onDisk, expected, StringComparison.Ordinal)) return;

            File.WriteAllText(path, expected, new UTF8Encoding(false));
            Log(GuardKind.HomeFile, "index.html",
                "首页文件被外部改动过（" + DescribeDiff(onDisk) + "），已还原成内嵌版本", true);
        }
        catch (Exception ex)
        {
            Log(GuardKind.HomeFile, "index.html", "自检失败：" + ex.Message, false);
        }
    }

    /// <summary>尽量说清楚「被改了什么」，说不清就退化成内容比对。</summary>
    private static string DescribeDiff(string onDisk)
    {
        var bits = new List<string>();
        if (!onDisk.Contains(Brand.SearchHostUrl, StringComparison.OrdinalIgnoreCase)) bits.Add("搜索引擎地址");
        if (!onDisk.Contains(Brand.SiteTl, StringComparison.OrdinalIgnoreCase)) bits.Add("tlstudio.cn 链接");
        if (!onDisk.Contains(Brand.SiteDoubler, StringComparison.OrdinalIgnoreCase)) bits.Add("tldoublerstudio.cn 链接");
        if (bits.Count > 0) return string.Join("、", bits) + " 不符";

        return LooksLikeUrl(onDisk)
            ? "内容与内嵌版本不一致，且文件里含外部网址"
            : "内容与内嵌版本不一致";
    }

    // ────────────────────────────── 4. 系统层面只读检查 ──────────────────────────────

    private const string IeMain = @"Software\Microsoft\Internet Explorer\Main";
    private const string IePolicy = @"Software\Policies\Microsoft\Internet Explorer\Control Panel";

    /// <summary>
    /// 看看这台机器上有没有「主页被改 / 被锁」，以及是谁干的。
    /// 全部只读，绝不修改任何东西。
    /// </summary>
    public static void ScanSystem()
    {
        SystemChecks.Clear();

        var startPage = ReadReg(Registry.CurrentUser, IeMain, "Start Page");
        var searchPage = ReadReg(Registry.CurrentUser, IeMain, "Search Page");

        if (string.IsNullOrEmpty(startPage))
            Note("主页项", "IE 主页", "没有设置", false);
        else if (IsHijackHost(startPage))
            Note("主页项", "IE 主页", $"被指向 {Shorten(startPage, 44)}　← 导航站", true,
                "这是导航站，主页劫持最常见的落点。\n点下面的「修复主页劫持」可以改回自己的站。");
        else
            Note("主页项", "IE 主页", Shorten(startPage, 60), false);

        if (!string.IsNullOrEmpty(searchPage))
        {
            if (IsHijackHost(searchPage))
                Note("搜索项", "IE 搜索页", $"被指向 {Shorten(searchPage, 44)}　← 导航站", true,
                    "搜索页也被改成了导航站，通常和主页是同一次劫持干的。");
            else
                Note("搜索项", "IE 搜索页", Shorten(searchPage, 60), false);
        }

        // 「主页锁定」就写在这两个值上：置 1 之后用户自己在 IE 选项里改不动
        var homeLock = ReadReg(Registry.CurrentUser, IePolicy, "HomePage");
        var searchLock = ReadReg(Registry.CurrentUser, IePolicy, "SearchScopes");
        if (IsOn(homeLock) || IsOn(searchLock))
            Note("锁定", "主页/搜索被锁定", $"锁定值 {Hint(homeLock, searchLock)}", true,
                $"注册表位置：HKCU\\{IePolicy}\n这就是「自己改不回来」的原因，通常由 360 的「主页防护」写入。");
        else
            Note("锁定", "主页/搜索锁定", "未锁定，可以自由修改", false);

        // 开机启动项里带网址的
        foreach (var (hive, tag) in new[]
                 {
                     (Registry.CurrentUser, "当前用户"),
                     (Registry.LocalMachine, "本机")
                 })
        {
            using var run = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (run is null) continue;
            foreach (var name in run.GetValueNames())
            {
                var v = run.GetValue(name)?.ToString() ?? "";
                if (LooksLikeUrl(v))
                    Note("启动项", "开机启动 " + Shorten(name, 30), $"[{tag}] {Shorten(v, 48)}", true,
                        $"注册表值 {name} 里带着一个网址：\n{v}\n\n开机启动项里带网址，多半就是在改主页。");
            }
        }

        // 启动文件夹里带网址的快捷方式
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(startup))
        {
            foreach (var lnk in SafeEnumerate(startup))
            {
                var args = ReadShortcutValue(lnk, "Arguments");
                if (LooksLikeUrl(args))
                    Note("启动项", "启动文件夹 " + Shorten(Path.GetFileNameWithoutExtension(lnk), 28),
                        Shorten(args, 48), true,
                        $"{lnk}\n启动参数：{args}");
            }
        }

        // 360 的残留：装过、卸载没卸干净，最容易留下一个被改过的主页
        var leftovers = new List<string>();
        using (var cu = Registry.CurrentUser.OpenSubKey(@"Software\360"))
            if (cu is not null)
                leftovers.AddRange(cu.GetSubKeyNames().Select(n => "HKCU\\Software\\360\\" + n));

        var installed = WhichKey(Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\360Safe", @"SOFTWARE\360Safe",
            @"SOFTWARE\WOW6432Node\360se6", @"SOFTWARE\360se6");

        if (installed.Length > 0)
            Note("360", "360 相关安装", $"发现 {installed}（只提示，不读取也不改动）", false);
        else if (leftovers.Count > 0)
            Note("360", "360 残留注册表",
                $"装过 360，程序已不在，注册表残留 {leftovers.Count} 项", true,
                "残留项：\n  " + string.Join("\n  ", leftovers) +
                "\n\n这类残留往往伴随一个被改过的 IE 主页。\n" +
                "本程序只提示，不会去删 360 的任何东西。");
        else
            Note("360", "360 残留", "没有发现", false);
    }

    /// <summary>导航站的域名。出现在主页/搜索项里基本可以断定不是用户自己设的。</summary>
    private static readonly string[] HijackRoots =
    {
        "hao.360.com", "hao.360.cn", "hao.360.cn", "www.360.cn", "360.cn",
        "hao123.com", "www.hao123.com", "2345.com", "hao.2345.com", "www.2345.com",
        "duba.com", "www.duba.com", "123.sogou.com", "so.com", "www.so.com",
        "union.360.cn", "qh.360.cn", "hao.duba.com", "msn.com", "www.msn.com"
    };

    private static bool IsHijackHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (Brand.IsBrandUrl(url)) return false;                  // 自己的站当然不算

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host.Length == 0) return false;

        return HijackRoots.Any(root =>
            host == root || host.EndsWith("." + root, StringComparison.Ordinal));
    }

    /// <summary>
    /// 修复主页劫持。只在用户明确点确认之后才调用。
    /// 只动 HKCU 下 IE 的主页/搜索项和主页锁定策略值，不碰任何程序文件、不卸载任何软件。
    /// </summary>
    public static string FixHomepageHijack(out string backupPath)
    {
        var done = new List<string>();
        backupPath = Path.Combine(Brand.AppDataDir,
            $"guard_backup_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var backup = new StringBuilder();
        backup.AppendLine("TL 浏览器 主页守护 · 改动前备份");
        backup.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        backup.AppendLine();

        // 1) 组策略锁定值
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(IePolicy, writable: true);
            if (key is not null)
            {
                foreach (var name in new[] { "HomePage", "SearchScopes", "Programs", "Connections" })
                {
                    var cur = key.GetValue(name);
                    if (cur is null) continue;
                    backup.AppendLine($"HKCU\\{IePolicy} :: {name} = {cur}");
                    try
                    {
                        key.DeleteValue(name, throwOnMissingValue: false);
                        done.Add($"删除 HKCU\\{IePolicy} → {name}（原值 {cur}）");
                    }
                    catch (Exception ex) { done.Add($"删除 {name} 失败：{ex.Message}"); }
                }
            }
        }
        catch { }

        // 2) 主页 / 搜索页
        var startPage = ReadReg(Registry.CurrentUser, IeMain, "Start Page");
        var searchPage = ReadReg(Registry.CurrentUser, IeMain, "Search Page");

        foreach (var name in new[] { "Start Page", "Search Page", "Default_Page_URL" })
        {
            var v = ReadReg(Registry.CurrentUser, IeMain, name);
            if (!string.IsNullOrEmpty(v))
                backup.AppendLine($"HKCU\\{IeMain} :: {name} = {v}");
        }

        if (IsHijackHost(startPage))
        {
            if (WriteReg(IeMain, "Start Page", Brand.SiteTl))
                done.Add($"IE 主页 {Shorten(startPage)} → {Brand.SiteTl}");
            else
                done.Add("改写 IE 主页失败");
        }

        if (IsHijackHost(searchPage))
        {
            const string bing = "https://www.bing.com/search?q=%s";
            if (WriteReg(IeMain, "Search Page", bing))
                done.Add($"IE 搜索页 {Shorten(searchPage)} → {bing}");
            else
                done.Add("改写 IE 搜索页失败");
        }

        try { File.WriteAllText(backupPath, backup.ToString(), new UTF8Encoding(false)); }
        catch { backupPath = "（备份写入失败）"; }

        foreach (var d in done) Log(GuardKind.System, "修复主页劫持", d, true);
        if (done.Count == 0)
            Log(GuardKind.System, "修复主页劫持", "没有发现需要处理的项目", false);

        return done.Count == 0 ? "没有发现需要处理的项目。" : string.Join("\r\n", done);
    }

    // ────────────────────────────── 工具 ──────────────────────────────

    private static string ReadReg(RegistryKey hive, string path, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            return k?.GetValue(name)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static bool WriteReg(string path, string name, string value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(path, writable: true);
            if (k is null) return false;
            k.SetValue(name, value, RegistryValueKind.String);
            return true;
        }
        catch { return false; }
    }

    private static string WhichKey(RegistryKey hive, params string[] paths)
    {
        foreach (var p in paths)
        {
            try
            {
                using var k = hive.OpenSubKey(p);
                if (k is not null) return p.Replace(@"SOFTWARE\", "").Replace(@"WOW6432Node\", "");
            }
            catch { }
        }
        return "";
    }

    private static string Hint(string? a, string? b)
    {
        var bits = new List<string>();
        if (IsOn(a)) bits.Add("HomePage=1");
        if (IsOn(b)) bits.Add("SearchScopes=1");
        return string.Join("、", bits);
    }

    private static bool IsOn(string? v) =>
        !string.IsNullOrWhiteSpace(v) && v.Trim() != "0";

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    private static string ReadShortcutValue(string lnk, string property)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return "";
        object? shell = null;
        object? sc = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return "";
            sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                null, shell, new object[] { lnk });
            return sc is null ? "" : Str(sc, property);
        }
        catch { return ""; }
        finally
        {
            if (sc is not null) { try { Marshal.FinalReleaseComObject(sc); } catch { } }
            if (shell is not null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
        }
    }

    private static string Str(object comObject, string property)
    {
        try
        {
            var v = comObject.GetType().InvokeMember(property,
                System.Reflection.BindingFlags.GetProperty, null, comObject, null);
            return v?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// 已知的顶级域。必须拿它来卡一道 —— 否则 "OneDrive.exe" 这种文件名
    /// 里也有个点、后缀也全是字母，会被当成网址，把报告淹成一片假警报。
    /// </summary>
    private static readonly HashSet<string> Tlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "cn", "net", "org", "gov", "edu", "mil", "int", "co", "biz", "info", "name", "mobi",
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn", "com.hk", "com.tw",
        "cc", "tv", "me", "io", "ai", "app", "dev", "pro", "top", "xyz", "vip", "shop", "store",
        "club", "site", "online", "wang", "ren", "ltd", "group", "tech", "art", "fun", "live",
        "link", "work", "space", "website", "host", "press", "pub", "wiki", "blog", "design"
    };

    /// <summary>
    /// 粗判一段文本里有没有网址。只用来做提示，不参与放行判断。
    /// 刻意不认 "C:\x\y.exe" 这种路径，也不认 -开关 /开关。
    /// </summary>
    private static bool LooksLikeUrl(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Contains("://", StringComparison.Ordinal)) return true;

        foreach (var raw in s.Split(new[] { ' ', '\t', '"', '\'', ',', ';', '|' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim();
            if (t.Length < 4) continue;
            if (t.Contains('\\')) continue;                    // 文件路径
            if (t[0] is '-' or '/') continue;                  // 命令行开关
            if (t.Contains("::")) continue;

            var cut = t.IndexOfAny(new[] { '/', '?', '#' });
            var host = cut >= 0 ? t[..cut] : t;
            if (host.Length == 0 || host.Length > 253) continue;

            var dot = host.LastIndexOf('.');
            if (dot <= 0 || dot == host.Length - 1) continue;
            if (!Tlds.Contains(host[(dot + 1)..])) continue;
            if (!host.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-')) continue;

            var labels = host[..dot].Split('.');
            if (labels.Length == 0) continue;
            if (!labels.All(IsLabel)) continue;

            return true;
        }
        return false;
    }

    private static bool IsLabel(string s) =>
        s.Length is > 0 and <= 63 && s[0] != '-' && s[^1] != '-' &&
        s.All(ch => char.IsLetterOrDigit(ch) || ch == '-');

    // ────────────────────────────── 5. 自检 ──────────────────────────────

    /// <summary>
    /// 样本表。<see cref="LooksLikeUrl"/> 和 <see cref="Brand.IsBrandUrl"/> 都是
    /// 一眼看不出对错的启发式，而且一旦改坏就是「要么漏拦、要么误报一片」，
    /// 所以留一份能跑的样本在这里，改完跑 --selftest 就知道有没有退化。
    /// </summary>
    private static readonly (string Input, bool Expect)[] UrlSamples =
    {
        ("https://hao.360.com/?src=lm&ls=", true),
        ("http://hao.360.cn", true),
        ("https://go.microsoft.com/fwlink/?LinkId=54896", true),
        ("www.2345.com/?k=abc", true),
        ("hao123.com", true),
        ("https://tlstudio.cn", true),
        // 以下这些以前会被误判成网址，把报告淹没成一片假警报
        (@"C:\Program Files\Microsoft OneDrive\OneDrive.exe", false),
        ("\"C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe\" /background", false),
        (@"D:\mgtv\MGTVPCC\芒果TV.exe act:7 src:1", false),
        (@"D:\Delta Force\launcher\startup_runner.exe", false),
        ("\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --no-startup-window", false),
        ("/background", false),
        ("notepad.exe", false),
        ("OneDrive.Sync.Service.exe", false),
        ("3.14", false),
        ("act:7", false),
        ("", false),
    };

    private static readonly (string Input, bool Expect)[] BrandSamples =
    {
        ("https://tlstudio.cn", true),
        ("https://tldoublerstudio.cn", true),
        ("http://www.tlstudio.cn/x", true),
        ("https://api.tlstudio.cn", true),
        // 关键：白名单判断必须按 host 分段，不能退化成 Contains
        ("http://eviltlstudio.cn", false),
        ("http://tlstudio.cn.evil.com", false),
        ("http://hao.360.cn", false),
        ("not a url", false),
        ("", false),
    };

    /// <summary>跑一遍样本表，返回失败条数（0 = 全对）。</summary>
    public static int SelfTest(out string reportText)
    {
        var sb = new StringBuilder();
        var bad = 0;

        int Run(string title, (string Input, bool Expect)[] samples, Func<string?, bool> fn)
        {
            sb.AppendLine($"【{title}】");
            var fails = 0;
            foreach (var (input, expect) in samples)
            {
                var got = fn(input);
                var ok = got == expect;
                if (!ok) { fails++; bad++; }
                sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  期望={(expect ? "是" : "否")}  " +
                              $"实际={(got ? "是" : "否")}  {Shorten(input)}");
            }
            sb.AppendLine($"  —— {samples.Length - fails}/{samples.Length} 通过");
            sb.AppendLine();
            return fails;
        }

        sb.AppendLine("TL 浏览器 主页守护 · 启发式自检");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();

        Run("LooksLikeUrl（判断一段文本里有没有网址）", UrlSamples, LooksLikeUrl);
        Run("IsBrandUrl（启动参数白名单，安全关键）", BrandSamples, Brand.IsBrandUrl);

        sb.AppendLine(new string('=', 70));
        sb.AppendLine(bad == 0
            ? $"全部通过（{UrlSamples.Length + BrandSamples.Length} 条）"
            : $"失败 {bad} 条");

        reportText = sb.ToString();
        return bad;
    }

    private static string Shorten(string s) => Shorten(s, 90);

    private static string Shorten(string s, int max) =>
        string.IsNullOrEmpty(s) ? "（空）" : s.Length <= max ? s : s[..Math.Max(1, max - 3)] + "...";
}
