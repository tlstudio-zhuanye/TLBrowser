using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace TLBrowser;

internal static class Program
{
    private const string MutexName = @"Local\TLSTUDIO.TLBrowser.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // 纯诊断入口：不起窗口、不加载内核，跑完就退。
        // 放在最前面，这样在没装 WebView2 的机器上也能跑。
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return RunSelfTest();

        // 界面语言要在任何窗口出现之前读，否则第一帧还是中文
        Lang.LoadState();

        // 拦截规则和开关先读进来，网页一开就要用
        Safety.Load();

        // 给验收脚本用的只读报告：规则条数、开关状态、以及一批拦截判定的样本结果
        if (args.Any(a => a.Equals("--blocktest", StringComparison.OrdinalIgnoreCase)))
            return RunBlockTest();

        // 企业模式 / 下载位置的判定样本。同样写报告、用退出码表示失败条数。
        if (args.Any(a => a.Equals("--kiosktest", StringComparison.OrdinalIgnoreCase)))
            return RunKioskTest();

        // 启动参数先过主页守护的白名单。往快捷方式里追加网址是外部程序改主页最常用的手法，
        // 白名单之外的参数在这里就被丢掉，起始页永远是本站首页。
        var startUrl = HomeGuard.GateStartUrl(args, out var dual, out var noSplash, out var openGuard);

        using var mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst && TryActivateExisting())
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        if (!EnsureWebView2Runtime()) return 0;

        // 先亮启动动画（不依赖 WebView2，所以是立刻出来的），
        // 主窗口在它背后安静地把内核和首页加载好，动画结束自然交接。
        SplashForm? splash = null;
        if (!noSplash)
        {
            splash = new SplashForm();
            splash.Show();
            splash.Update();
        }

        // 主页守护自检放在动画背后跑，不额外拖慢启动观感
        RunGuardChecks();

        var main = new BrowserForm(startUrl, dual, openGuard);
        if (splash is not null)
        {
            splash.FormClosed += (_, _) =>
            {
                try { main.Activate(); } catch { }
            };
        }

        Application.Run(main);

        if (splash is not null && !splash.IsDisposed) splash.Dispose();
        HistoryStore.Flush();
        GC.KeepAlive(mutex);
        return 0;
    }

    /// <summary>
    /// --selftest：跑一遍主页守护的启发式样本，把报告写进数据目录，退出码即失败条数。
    /// 给验收脚本和「改完想快速确认没退化」用。
    /// </summary>
    private static int RunSelfTest()
    {
        var bad = HomeGuard.SelfTest(out var text);
        try
        {
            File.WriteAllText(Path.Combine(Brand.AppDataDir, "selftest.txt"),
                text, new System.Text.UTF8Encoding(false));
        }
        catch { }
        return bad;
    }

    /// <summary>
    /// 拦截规则的样本表。期望值写在左边，实际判定写在右边。
    /// 重点是那些「看起来该拦但不能拦」的对照项——后缀匹配写成 Contains
    /// 时 eviltlstudio.cn / notdoubleclick.net 就会漏，这几条专门抓这个。
    /// </summary>
    private static readonly (string Url, bool Block)[] BlockSamples =
    {
        // 该拦的
        ("https://ad.doubleclick.net/ddm/adi/banner.gif", true),
        ("https://securepubads.g.doubleclick.net/tag/js/gpt.js", true),
        ("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js", true),
        ("https://www.google-analytics.com/analytics.js", true),
        ("https://hm.baidu.com/hm.js?abc123", true),
        ("https://cnzz.com/stat.htm", true),
        ("https://ads.example.com/banner.png", true),
        ("https://ad.example.com/track.gif", true),
        ("https://analytics.example.com/collect", true),
        ("https://union.360.cn/union/stat", true),
        ("https://afp.iqiyi.com/v1/ad", true),

        // 不能拦的
        ("https://tlstudio.cn/", false),
        ("https://www.tlstudio.cn/index.html", false),
        ("https://tldoublerstudio.cn/", false),
        ("https://api.tlstudio.cn/v1/hello", false),
        ("https://eviltlstudio.cn/a.js", false),
        ("https://tlstudio.cn.evil.com/a.js", false),
        ("https://notdoubleclick.net/a.gif", false),
        ("https://adobe.com/", false),
        ("https://tickets.example.com/a.png", false),
        ("https://img.alicdn.com/logo.png", false),
        ("https://cdn.jsdelivr.net/npm/vue.js", false),
        ("https://www.bing.com/search?q=1", false),
        ("https://www.baidu.com/", false),
    };

    /// <summary>
    /// --blocktest：把样本表跑一遍，报告写进 blocktest.txt，退出码 = 失败条数。
    /// 拦截规则很容易「写了个看着对、其实不生效」的后缀判断，所以留了这么一个能证伪的入口。
    /// </summary>
    private static int RunBlockTest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TL 浏览器 · 广告与跟踪拦截判定自检");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine($"规则条数：{Safety.RuleCount}　拦截开关：{(Safety.AdBlock ? "开" : "关")}");
        sb.AppendLine(new string('=', 70));

        var bad = 0;
        foreach (var (url, expect) in BlockSamples)
        {
            var got = Safety.Evaluate(url);
            var ok = got == expect;
            if (!ok) bad++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] 期望={(expect ? "拦" : "放")} " +
                          $"实际={(got ? "拦" : "放")}  {url}");
        }

        sb.AppendLine(new string('=', 70));
        sb.AppendLine(bad == 0
            ? $"全部通过（{BlockSamples.Length} 条）"
            : $"{BlockSamples.Length - bad} 条通过，{bad} 条失败");

        try
        {
            File.WriteAllText(Path.Combine(Brand.AppDataDir, "blocktest.txt"),
                sb.ToString(), new UTF8Encoding(false));
        }
        catch { }

        return bad;
    }

    /// <summary>
    /// 企业模式（网站白名单）里那份「允许访问的网站」，自测用。
    /// 特意混了域名、带非标端口的 IP、以及只加了一半的自家站
    /// —— 这三类最容易在匹配逻辑里写错。
    /// </summary>
    private static readonly string[] KioskAllowed =
    {
        "https://oa.tlstudio.cn/",     // 第一条 = 启动页
        "http://10.0.0.5:8080/",       // 内网系统：IP + 非标端口
        "https://tlstudio.cn/",        // 官网（子域一起放行）
    };

    /// <summary>
    /// 企业模式的放行判定样本。重点是那些「看起来该拦却会被放过」的地址：
    /// 域名判定一旦写成 Contains，eviltlstudio.cn 就能混过 tlstudio.cn；
    /// 子域放行必须是单向的 —— 填 oa.tlstudio.cn 不能把整个 tlstudio.cn 都放进来。
    /// 用纯函数 Kiosk.AllowsUrl，不读也不写真实配置。
    /// </summary>
    private static readonly (string Url, bool Expect)[] KioskSamples =
    {
        // 名单里的站
        ("https://oa.tlstudio.cn/", true),
        ("https://oa.tlstudio.cn/login?u=1", true),
        ("https://mail.tlstudio.cn/", true),          // tlstudio.cn 的子域
        ("https://tlstudio.cn/about", true),
        ("http://10.0.0.5:8080/erp", true),
        ("https://10.0.0.5:8080/", true),             // 协议不参与判定
        ("https://OA.TLSTUDIO.CN/", true),            // 大小写不敏感
        // 名单之外的一律拦下
        ("http://10.0.0.5:9090/", false),             // 同一个 IP，端口不在名单
        ("https://tldoublerstudio.cn", false),        // 自家另一个站，没加就不放
        ("https://eviltlstudio.cn", false),           // 前缀相似
        ("https://tlstudio.cn.evil.com", false),      // 拿自家域名当子域骗
        ("https://oa.tlstudio.cn.evil.com", false),   // 同上，前面还加了个 oa
        ("https://hao.360.cn", false),
        ("https://www.baidu.com", false),
        ("https://github.com", false),
        // 浏览器自己的页面不算「跑出去」
        ("about:blank", true),
        ("https://tlstudio.invalid/newtab", true),    // 新标签页
        ("file:///C:/x.html", false),
        ("", false),
    };

    /// <summary>--kiosktest：跑企业模式判定样本，报告写 kiosktest.txt，退出码 = 失败条数。</summary>
    private static int RunKioskTest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TL 浏览器 · 企业模式判定自检");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();

        var bad = 0;

        sb.AppendLine("A. 白名单 3 条（域名 / 带非标端口的 IP / 自家官网），放行子域名");
        foreach (var (url, expect) in KioskSamples)
        {
            var got = Kiosk.AllowsUrl(url, KioskAllowed, true);
            var ok = got == expect;
            if (!ok) bad++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] 期望={(expect ? "放行" : "拦下")} " +
                          $"实际={(got ? "放行" : "拦下")}  {(url.Length == 0 ? "（空）" : url)}");
        }
        sb.AppendLine();

        // 关掉子域放行后，只有名单里写死的那些主机能进（连 mail. 这种子域也拦）
        sb.AppendLine("B. 同一份名单，但关掉「放行子域名」");
        foreach (var (url, expect) in new[] {
            ("https://oa.tlstudio.cn/", true),
            ("https://tlstudio.cn/", true),
            ("http://10.0.0.5:8080/x", true),
            ("https://mail.tlstudio.cn/", false),
            ("https://eviltlstudio.cn", false),
        })
        {
            var got = Kiosk.AllowsUrl(url, KioskAllowed, false);
            var ok = got == expect;
            if (!ok) bad++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] 期望={(expect ? "放行" : "拦下")} " +
                          $"实际={(got ? "放行" : "拦下")}  {url}");
        }
        sb.AppendLine();

        // 下载位置的同名排队：不能把旧文件覆盖掉
        sb.AppendLine("C. 下载位置 · 同名文件排队");
        var dir = Path.Combine(Path.GetTempPath(), "tlb_kiosktest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var first = Path.Combine(dir, "a.txt");
            File.WriteAllText(first, "1");
            var p1 = DownloadStore.UniquePath(dir, "a.txt");
            File.WriteAllText(p1, "2");
            var p2 = DownloadStore.UniquePath(dir, "a.txt");
            File.WriteAllText(p2, "3");
            var p3 = DownloadStore.UniquePath(dir, "a.txt");

            var names = new[] { Path.GetFileName(p1), Path.GetFileName(p2), Path.GetFileName(p3) };
            var ok = names[0] == "a (1).txt" && names[1] == "a (2).txt" && names[2] == "a (3).txt";
            if (!ok) bad++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] 同名文件依次排队为 " +
                          $"{string.Join(" / ", names)}（期望 a (1).txt / a (2).txt / a (3).txt）");
        }
        catch (Exception ex)
        {
            bad++;
            sb.AppendLine($"  [FAIL] 排队测试异常：{ex.Message}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
        sb.AppendLine();

        sb.AppendLine(new string('=', 70));
        sb.AppendLine(bad == 0 ? "全部通过" : $"失败 {bad} 条");

        try
        {
            File.WriteAllText(Path.Combine(Brand.AppDataDir, "kiosktest.txt"),
                sb.ToString(), new UTF8Encoding(false));
        }
        catch { }

        return bad;
    }

    /// <summary>
    /// 主页守护的三步自检。顺序有讲究：必须先比对磁盘上的首页（好在被改过时留个记录），
    /// 再统一覆盖写盘；反过来就永远发现不了改动。
    /// </summary>
    private static void RunGuardChecks()
    {
        // 主页不再"先比对磁盘文件再覆盖写盘"：磁盘上已经没有首页文件了，
        // 页面是内存里现场生成的。这里只校验生成器自己的输出有没有跑偏。
        try { HomeGuard.CleanupObsoleteHome(); } catch { }
        try { HomeGuard.VerifyHomeContent(); } catch { }
        try { HomeGuard.HealShortcuts(); } catch { }
        try { HomeGuard.ScanSystem(); } catch { }
    }

    /// <summary>内核缺失时给出可操作的引导，而不是直接崩掉。</summary>
    private static bool EnsureWebView2Runtime()
    {
        string? version = null;
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException) { }
        catch (Exception) { }

        if (!string.IsNullOrEmpty(version)) return true;

        var answer = MessageBox.Show(
            Lang.T("boot.noRuntime"),
            Lang.T("boot.noRuntimeTitle", Brand.AppName),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (answer == DialogResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(Brand.DownloadPage) { UseShellExecute = true }); }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// 已有实例就把它的窗口提到前面。找不到窗口（比如上一个实例正在启动或正在退出）
    /// 就返回 false，让本次照常启动自己——总比双击后什么都没发生强。
    /// </summary>
    private static bool TryActivateExisting()
    {
        for (var i = 0; i < 30; i++)
        {
            var hwnd = FindWindow(null, Brand.AppName);
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, 9);            // SW_RESTORE
                SetForegroundWindow(hwnd);
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private static void ReportCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TLSTUDIO", "TLBrowser");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }

        MessageBox.Show("程序遇到问题：\n\n" + ex.Message + "\n\n详细信息已写入 crash.log",
            Brand.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
