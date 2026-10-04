using System.Diagnostics;
using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TLBrowser;

/// <summary>一个标签页。可以是单页（PageTab），也可以是左右并排的双站对照（DualTab）。</summary>
internal abstract class WebTab : UserControl
{
    protected readonly List<WebView2> Views = new();
    private readonly HashSet<WebView2> _loading = new();

    protected WebView2? ActiveView;

    private CoreWebView2Environment? _env;

    /// <summary>
    /// 当前顶层文档的地址。拦截里要用它把「主文档」和「子框架」区分开：
    /// 顶层永远放行，免得出现点开一片白。
    /// </summary>
    private string? _topLevelUri;

    /// <summary>1×1 全透明 GIF。被拦掉的图片用这个顶替，页面不会出现破图。</summary>
    private static readonly byte[] TransparentGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02,
        0x44, 0x01, 0x00, 0x3B
    };

    public string Title { get; protected set; } = Lang.T("tab.newTitle");
    public Image? Favicon { get; protected set; }
    public bool IsLoading { get; private set; }
    public bool CanGoBack { get; private set; }
    public bool CanGoForward { get; private set; }
    public string Address { get; protected set; } = "";
    public string StatusText { get; protected set; } = "";
    public bool IsHomePage { get; protected set; } = true;
    public double ZoomFactor { get; private set; } = 1.0;

    public abstract bool IsDual { get; }

    /// <summary>标签状态发生变化（标题 / 图标 / 地址 / 前进后退 / 加载中 / 缩放）。</summary>
    public event Action<WebTab>? Changed;

    /// <summary>页面请求开新窗口（target="_blank" 等），交由主窗口新开标签。</summary>
    public event Action<WebTab, string>? NewTabRequested;

    /// <summary>把在网页里按下的键交给主窗口处理；返回 true 表示已处理并拦截。</summary>
    public Func<uint, bool>? KeyRouter;

    /// <summary>
    /// 网页右键菜单里点了翻译。参数一 = 选中的文字（null 表示没有选中，要翻整页），
    /// 参数二 = 当前页面地址。交给主窗口去开翻译标签，标签自己不认得主窗口。
    /// </summary>
    public Action<string?, string?>? TranslateRequested;

    protected void RaiseChanged() => Changed?.Invoke(this);

    protected abstract void BuildViews();

    public async Task InitAsync(CoreWebView2Environment env, bool goHome = true)
    {
        _env = env;
        BuildViews();
        foreach (var view in Views)
        {
            await view.EnsureCoreWebView2Async(env);
            ConfigureCore(view.CoreWebView2!);
            Hook(view);
        }
        ActiveView = Views[0];
        ZoomFactor = ActiveView.ZoomFactor;
        UpdateActiveIndicators();
        if (goHome) GoHome();
        RaiseChanged();
    }

    public abstract void GoHome();

    public void Navigate(string url)
    {
        if (url == Brand.HomeUrl) { GoHome(); return; }
        ActiveView?.CoreWebView2?.Navigate(url);
    }

    public void GoBack()
    {
        var c = ActiveView?.CoreWebView2;
        if (c is { CanGoBack: true }) c.GoBack();
    }

    public void GoForward()
    {
        var c = ActiveView?.CoreWebView2;
        if (c is { CanGoForward: true }) c.GoForward();
    }

    public void ReloadOrStop()
    {
        var c = ActiveView?.CoreWebView2;
        if (c is null) return;
        if (IsLoading) c.Stop(); else c.Reload();
    }

    public void Stop() => ActiveView?.CoreWebView2?.Stop();

    public void FocusContent()
    {
        if (ActiveView is null) return;
        ActiveView.Focus();
        try
        {
            WebView2Internals.GetController(ActiveView)
                ?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
        catch { }
    }

    public void SetZoom(double z)
    {
        if (ActiveView is null) return;
        ActiveView.ZoomFactor = Math.Clamp(z, 0.25, 4.0);
    }

    public void ResetZoom() => SetZoom(1.0);

    /// <summary>标签静音。有些页面自动播放视频，直接把它掐掉比关页面省事。</summary>
    public bool IsMuted
    {
        get
        {
            try { return ActiveView?.CoreWebView2?.IsMuted ?? false; }
            catch { return false; }
        }
        set
        {
            try
            {
                if (ActiveView?.CoreWebView2 is not { } c) return;
                c.IsMuted = value;
                RaiseChanged();
            }
            catch { }
        }
    }

    public void ToggleMute() => IsMuted = !IsMuted;

    public void OpenDevTools()
    {
        try { ActiveView?.CoreWebView2?.OpenDevToolsWindow(); } catch { }
    }

    public void Print()
    {
        try { ActiveView?.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch { }
    }

    public string CurrentUrl => ActiveView?.CoreWebView2?.Source ?? Address;

    // ────────────────────────────── 内部实现 ──────────────────────────────

    private static void ConfigureCore(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = true;
        s.AreDevToolsEnabled = true;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = true;
        s.IsSwipeNavigationEnabled = true;
        s.IsBuiltInErrorPageEnabled = true;
        s.IsPasswordAutosaveEnabled = true;
        s.IsGeneralAutofillEnabled = true;

        // 内核层强制亮色：系统深色主题时不给任何页面套暗色，
        // 主页加载瞬间也不会先闪一屏黑。个别属性旧运行时没有，静默跳过。
        try { core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light; }
        catch { }
    }

    private void Hook(WebView2 view)
    {
        var core = view.CoreWebView2!;

        core.NavigationStarting += (_, e) =>
        {
            // 外部协议（mailto: / ms-* / 各种自家协议）是钓鱼页最常用的跳板：
            // 点一下就可能拉起一个「安装程序」。默认不直接开，先问，默认按钮是「否」。
            if (!IsNavigableScheme(e.Uri))
            {
                e.Cancel = true;
                AskExternal(e.Uri);
                return;
            }

            _topLevelUri = e.Uri;
            MarkActive(view);
            _loading.Add(view);
            RecalcLoading();
            Address = e.Uri;
            IsHomePage = IsHomePageUrl(e.Uri);
            StatusText = Lang.T("tab.opening", SafeHost(e.Uri));
            RaiseChanged();
        };

        core.SourceChanged += (_, _) =>
        {
            if (view != ActiveView) return;
            Address = core.Source ?? Address;
            IsHomePage = IsHomePageUrl(core.Source);
            RaiseChanged();
        };

        core.NavigationCompleted += (_, e) =>
        {
            _loading.Remove(view);
            RecalcLoading();
            if (view == ActiveView)
            {
                Address = core.Source ?? Address;
                IsHomePage = IsHomePageUrl(core.Source);
                CanGoBack = core.CanGoBack;
                CanGoForward = core.CanGoForward;
                StatusText = e.IsSuccess ? "" : Lang.T("tab.loadFailed", e.WebErrorStatus);
            }
            if (!IsDual) Title = CleanTitle(core.DocumentTitle);

            // 只有真的加载成功才记历史：失败的页面记下来，联想里会一直冒出打不开的地址
            if (e.IsSuccess)
            {
                var t = core.DocumentTitle ?? "";
                HistoryStore.Add(core.Source, string.IsNullOrWhiteSpace(t) ? null : t);
            }

            RaiseChanged();
        };

        core.HistoryChanged += (_, _) =>
        {
            if (view != ActiveView) return;
            CanGoBack = core.CanGoBack;
            CanGoForward = core.CanGoForward;
            RaiseChanged();
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            MarkActive(view);
            if (!IsDual) Title = CleanTitle(core.DocumentTitle);
            RaiseChanged();
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrEmpty(e.Uri)) return;

            // 不是用户自己点出来的新窗口，就是弹窗广告。拦掉，连标签都不开。
            if (Safety.BlockAutoPopups)
            {
                var byUser = true;
                try { byUser = e.IsUserInitiated; } catch { }
                if (!byUser)
                {
                    Safety.CountPopupBlocked(e.Uri);
                    RaiseChanged();
                    return;
                }
            }

            NewTabRequested?.Invoke(this, e.Uri);
        };

        core.FaviconChanged += async (_, _) =>
        {
            try
            {
                await using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (stream is null) return;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                Favicon = Image.FromStream(ms);
                RaiseChanged();
            }
            catch { }
        };

        core.StatusBarTextChanged += (_, _) =>
        {
            if (view != ActiveView) return;
            StatusText = core.StatusBarText ?? "";
            RaiseChanged();
        };

        core.ProcessFailed += (_, e) =>
        {
            _loading.Remove(view);
            RecalcLoading();
            StatusText = Lang.T("tab.crashed", e.ProcessFailedKind);
            RaiseChanged();
        };

        var ctl = WebView2Internals.GetController(view);
        if (ctl is not null)
        {
            ctl.AcceleratorKeyPressed += (_, e) =>
            {
                if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown))
                    return;
                MarkActive(view);
                if (KeyRouter?.Invoke(e.VirtualKey) == true) e.Handled = true;
            };
        }

        view.ZoomFactorChanged += (_, _) =>
        {
            if (view != ActiveView) return;
            ZoomFactor = view.ZoomFactor;
            RaiseChanged();
        };

        // 点进哪一侧，就把哪一侧当“当前侧”（双站对照时决定地址栏跟谁走）
        view.GotFocus += (_, _) => MarkActive(view);
        view.Enter += (_, _) => MarkActive(view);
        core.ContextMenuRequested += (_, e) =>
        {
            MarkActive(view);
            AddTranslateMenuItems(core, e);
        };

        InstallSafety(core);
    }

    /// <summary>
    /// 往网页自己的右键菜单里补两项：「翻译选中的文字」（有选区才出现）和「翻译此页」。
    ///
    /// 刻意不接管整个菜单——只往末尾追加，系统原有的复制/粘贴/检查照旧，
    /// 免得用户觉得右键被改坏了。
    /// </summary>
    private void AddTranslateMenuItems(CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (_env is null || TranslateRequested is null) return;

        try
        {
            var target = e.ContextMenuTarget;

            var selected = target.HasSelection ? target.SelectionText : null;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                var t = selected!.Trim();
                if (t.Length > 500) t = t[..500];
                e.MenuItems.Add(MakeMenuItem(Lang.T("menu.translateText"), null, t));
            }

            var page = target.PageUri;
            if (!string.IsNullOrWhiteSpace(page) &&
                !page!.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                !page.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                e.MenuItems.Add(MakeMenuItem(Lang.T("menu.translatePage"), page, null));
            }
        }
        catch
        {
            // 右键菜单加不进去就算了，绝不能因为翻译把右键搞没了
        }
    }

    private CoreWebView2ContextMenuItem MakeMenuItem(string label, string? pageUrl, string? selectedText)
    {
        var item = _env!.CreateContextMenuItem(label, null, CoreWebView2ContextMenuItemKind.Command);
        item.CustomItemSelected += (_, _) => TranslateRequested?.Invoke(selectedText, pageUrl);
        return item;
    }

    // ────────────────────────────── 安全与拦截 ──────────────────────────────

    /// <summary>
    /// 装上本浏览器的四道防护。全部挂在 CoreWebView2 上，不需要额外的进程或驱动。
    /// </summary>
    private void InstallSafety(CoreWebView2 core)
    {
        InstallAdBlock(core);
        InstallPermissionGuard(core);
        InstallDownloadGuard(core);
        InstallExternalSchemeGuard(core);
    }

    /// <summary>
    /// 广告与跟踪拦截。
    ///
    /// 只挂 子资源 + Document（子框架）这几类，而且 Document 那一路还要再确认
    /// 「这不是顶层文档」才允许拦——顶层永远放行。这样最坏是少个广告位，
    /// 不会把用户正在看的页面整个拦掉。
    /// </summary>
    private void InstallAdBlock(CoreWebView2 core)
    {
        if (_env is null) return;

        var contexts = new[]
        {
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceContext.Script,
            CoreWebView2WebResourceContext.Stylesheet,
            CoreWebView2WebResourceContext.Media,
            CoreWebView2WebResourceContext.Font,
            CoreWebView2WebResourceContext.XmlHttpRequest,
            CoreWebView2WebResourceContext.Fetch,
            CoreWebView2WebResourceContext.Document,
        };

        foreach (var ctx in contexts)
        {
            try { core.AddWebResourceRequestedFilter("*", ctx); } catch { }
        }

        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                if (!Safety.AdBlock) return;

                var uri = e.Request?.Uri;
                if (string.IsNullOrEmpty(uri)) return;

                // 顶层文档放行。子框架（iframe）才参与拦截——很多广告就是 iframe。
                if (e.ResourceContext == CoreWebView2WebResourceContext.Document &&
                    string.Equals(uri, _topLevelUri, StringComparison.OrdinalIgnoreCase)) return;

                if (!Safety.ShouldBlock(uri)) return;

                var response = BlockedResponse(e.ResourceContext);
                if (response is null) return;

                Safety.CountBlocked(uri);
                e.Response = response;
                RaiseChanged();
            }
            catch { }
        };
    }

    /// <summary>
    /// 被拦掉的东西不能回 403——那样页面会出现破图、脚本报错。
    /// 按类型回一个「空的但格式合法」的东西，页面会安静地当它不存在。
    /// </summary>
    private CoreWebView2WebResourceResponse? BlockedResponse(CoreWebView2WebResourceContext ctx)
    {
        if (_env is null) return null;

        try
        {
            switch (ctx)
            {
                case CoreWebView2WebResourceContext.Image:
                    return _env.CreateWebResourceResponse(
                        new MemoryStream(TransparentGif), 200, "OK", "Content-Type: image/gif");

                case CoreWebView2WebResourceContext.Script:
                    return _env.CreateWebResourceResponse(
                        new MemoryStream(Array.Empty<byte>()), 200, "OK", "Content-Type: application/javascript");

                case CoreWebView2WebResourceContext.Stylesheet:
                    return _env.CreateWebResourceResponse(
                        new MemoryStream(Array.Empty<byte>()), 200, "OK", "Content-Type: text/css");

                default:
                    return _env.CreateWebResourceResponse(
                        new MemoryStream(Array.Empty<byte>()), 200, "OK", "Content-Type: text/plain");
            }
        }
        catch { return null; }
    }

    /// <summary>摄像头 / 麦克风 / 定位 / 通知这类权限：默认一律拒绝，只给自家站点开口子。</summary>
    private void InstallPermissionGuard(CoreWebView2 core)
    {
        core.PermissionRequested += (_, e) =>
        {
            try
            {
                if (!Safety.DenyPermissions) return;      // 关掉就交回内核默认行为

                var host = Safety.HostOf(e.Uri ?? "");
                if (Brand.IsBrandHostName(host))
                {
                    e.State = CoreWebView2PermissionState.Allow;
                    return;
                }

                e.State = CoreWebView2PermissionState.Deny;
                e.SavesInProfile = false;                 // 不记进档案，免得以后改主意改不回来
                Safety.CountPermissionDenied(e.PermissionKind.ToString(), host);
                RaiseChanged();
            }
            catch { }
        };
    }

    /// <summary>
    /// 下载先问一句（这是防「页面偷偷往你硬盘里塞东西」最直接的一道），
    /// 然后接管下载：不弹系统的「另存为」，直接下到下载文件夹，进度进下载面板。
    /// </summary>
    private void InstallDownloadGuard(CoreWebView2 core)
    {
        core.DownloadStarting += (_, e) =>
        {
            try
            {
                var op = e.DownloadOperation;
                var uri = op?.Uri ?? "";
                var name = Path.GetFileName(e.ResultFilePath ?? "");
                if (string.IsNullOrEmpty(name)) name = Path.GetFileName(uri);
                if (string.IsNullOrEmpty(name)) name = Lang.T("dl.unknownFile");

                if (Safety.ConfirmDownload)
                {
                    var answer = MessageBox.Show(
                        $"文件名：{name}\n来源：{Safety.HostOf(uri)}\n\n" +
                        "将保存到系统的「下载」文件夹，进度可在下载面板里查看。\n只下载你自己要的东西。确定下载吗？",
                        Brand.AppName + " · 下载确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

                    if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                }

                // 接管：不显示 WebView2 默认的「另存为」，统一走下载面板
                e.Handled = true;

                if (op is not null)
                {
                    DownloadStore.Track(op, name, uri);
                    StatusText = Lang.T("dl.started", name);
                    RaiseChanged();
                }
            }
            catch { }
        };
    }

    /// <summary>外部协议跳转：先把决定权交回用户，默认不开。</summary>
    private void InstallExternalSchemeGuard(CoreWebView2 core)
    {
        try
        {
            core.LaunchingExternalUriScheme += (_, e) =>
            {
                var uri = e.Uri ?? "";
                if (!Safety.AskExternalScheme) return;

                e.Cancel = !ConfirmExternal(uri);
                if (!e.Cancel) CountAndOpen(uri);
            };
        }
        catch { }   // 旧版运行时没有这个事件，退回到 NavigationStarting 那条路
    }

    private static bool ConfirmExternal(string uri)
    {
        try
        {
            var answer = MessageBox.Show(
                Lang.T("ext.confirmBody", uri),
                Lang.T("ext.confirmTitle", Brand.AppName),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return answer == DialogResult.Yes;
        }
        catch { return false; }
    }

    private static void CountAndOpen(string uri)
    {
        Safety.CountExternalAsked(uri);
        OpenExternal(uri);
    }

    private void AskExternal(string uri)
    {
        try
        {
            if (!Safety.AskExternalScheme) { CountAndOpen(uri); return; }
            if (ConfirmExternal(uri)) CountAndOpen(uri);
            RaiseChanged();
        }
        catch { }
    }

    private void RecalcLoading()
    {
        IsLoading = _loading.Count > 0;
    }

    protected void MarkActive(WebView2 view)
    {
        if (ActiveView == view) return;
        ActiveView = view;
        UpdateActiveIndicators();
        Address = view.CoreWebView2?.Source ?? Address;
        IsHomePage = IsHomePageUrl(Address);
        CanGoBack = view.CoreWebView2?.CanGoBack ?? false;
        CanGoForward = view.CoreWebView2?.CanGoForward ?? false;
        RaiseChanged();
    }

    /// <summary>双站对照时高亮当前侧。</summary>
    protected virtual void UpdateActiveIndicators() { }

    protected static bool IsHomePageUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
        url.Contains("/home/index.html", StringComparison.OrdinalIgnoreCase);

    private static bool IsNavigableScheme(string uri) =>
        uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("edge:", StringComparison.OrdinalIgnoreCase);

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    private static string SafeHost(string uri)
    {
        try { return new Uri(uri).Host; } catch { return uri; }
    }

    private static string CleanTitle(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return Lang.T("tab.newTitle");
        t = t.Trim();
        return t.Length > 120 ? t[..120] : t;
    }
}

/// <summary>普通单页标签。</summary>
internal sealed class PageTab : WebTab
{
    private WebView2 _view = null!;

    public override bool IsDual => false;

    protected override void BuildViews()
    {
        _view = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_view);
        Views.Add(_view);
    }

    public override void GoHome() => _view.CoreWebView2?.Navigate(Brand.HomeUrl);
}

/// <summary>招牌功能：一个标签里左右并排 TL 工作室 + TL Doubler Studio。</summary>
internal sealed class DualTab : WebTab
{
    private SplitContainer? _split;
    private WebView2 _left = null!;
    private WebView2 _right = null!;
    private Panel _leftHeader = null!;
    private Panel _rightHeader = null!;
    private bool _adjusting;

    public override bool IsDual => true;

    protected override void BuildViews()
    {
        Title = Lang.T("tab.dualTitle");

        // SplitContainer 的 Panel1MinSize / Panel2MinSize / SplitterDistance
        // 不能在构造期就设：那时控件宽度还是默认的 150，会抛
        // "SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize"。
        // 所以等它有真实宽度后再调，见 ApplySplit()。
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = Brand.ChromeLine
        };

        _left = new WebView2 { Dock = DockStyle.Fill };
        _right = new WebView2 { Dock = DockStyle.Fill };

        _leftHeader = MakeHeader("tlstudio.cn", Brand.Blue);
        _rightHeader = MakeHeader("tldoublerstudio.cn", Brand.Purple);

        _split.Panel1.Controls.Add(_left);
        _split.Panel1.Controls.Add(_leftHeader);
        _split.Panel2.Controls.Add(_right);
        _split.Panel2.Controls.Add(_rightHeader);

        _leftHeader.Click += (_, _) => Activate(_left);
        _rightHeader.Click += (_, _) => Activate(_right);
        _leftHeader.Cursor = Cursors.Hand;
        _rightHeader.Cursor = Cursors.Hand;

        Controls.Add(_split);
        Views.Add(_left);
        Views.Add(_right);

        _split.SizeChanged += (_, _) => ApplySplit();
        ApplySplit();
    }

    /// <summary>给左右两栏设最小宽度并居中分隔条；宽度不够时按比例缩小下限。</summary>
    private void ApplySplit()
    {
        if (_adjusting || _split is null || _split.IsDisposed) return;
        var w = _split.Width;
        if (w < 120) return;

        _adjusting = true;
        try
        {
            var min = Math.Min(180, Math.Max(40, w / 3));
            _split.Panel1MinSize = min;
            _split.Panel2MinSize = min;
            var half = (w - _split.SplitterWidth) / 2;
            _split.SplitterDistance = Math.Max(min, Math.Min(w - min - _split.SplitterWidth, half));
        }
        catch { }
        finally { _adjusting = false; }
    }

    private void Activate(WebView2 view)
    {
        MarkActive(view);
        view.Focus();
    }

    public override void GoHome()
    {
        _left.CoreWebView2?.Navigate(Brand.SiteTl);
        _right.CoreWebView2?.Navigate(Brand.SiteDoubler);
    }

    /// <summary>左右互换。</summary>
    public void SwapSides()
    {
        var a = _left.CoreWebView2?.Source;
        var b = _right.CoreWebView2?.Source;
        if (a is not null) _right.CoreWebView2?.Navigate(a);
        if (b is not null) _left.CoreWebView2?.Navigate(b);
    }

    protected override void UpdateActiveIndicators()
    {
        if (_leftHeader is null || _rightHeader is null) return;
        PaintHeader(_leftHeader, Brand.Blue, ReferenceEquals(ActiveView, _left));
        PaintHeader(_rightHeader, Brand.Purple, ReferenceEquals(ActiveView, _right));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplySplit();
    }

    private static Panel MakeHeader(string text, Color accent)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.White };
        var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            ForeColor = Brand.TextDim,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        panel.Controls.Add(label);
        panel.Controls.Add(bar);
        return panel;
    }

    private static void PaintHeader(Panel panel, Color accent, bool active)
    {
        panel.BackColor = active
            ? Color.FromArgb((accent.R + 255 * 5) / 6, (accent.G + 255 * 5) / 6, (accent.B + 255 * 5) / 6)
            : Color.White;
        foreach (Control c in panel.Controls)
            if (c is Label l) l.ForeColor = active ? Brand.TextMain : Brand.TextDim;
    }
}
