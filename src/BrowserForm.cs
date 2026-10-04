using System.Diagnostics;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace TLBrowser;

internal sealed class BrowserForm : Form
{
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Panel _chrome = new() { Dock = DockStyle.Top, Height = 84, BackColor = Brand.ChromeBg };
    private readonly TabStrip _tabStrip = new();
    private readonly Panel _toolbar = new() { Dock = DockStyle.Top, Height = 44, BackColor = Brand.ChromeBg };
    private readonly StatusBarPanel _statusBar = new() { Dock = DockStyle.Bottom, Height = 24 };

    // 状态条用显式定位而不用 Dock：三个控件都往右靠，
    // Dock 的排布顺序取决于 z-order，不如自己算清楚
    private readonly Label _statusText = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0),
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 8.25f),
        BackColor = Brand.StatusBg
    };
    private readonly Label _zoomLabel = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(0, 0, 6, 0),
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 8.25f),
        BackColor = Brand.StatusBg
    };
    private readonly ShieldButton _shield = new();

    /// <summary>状态条上的广告拦截计数。没拦到东西就不显示——空着比写「0 条」干净。</summary>
    private readonly Label _blockLabel = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(0, 0, 4, 0),
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 8.25f),
        BackColor = Brand.StatusBg,
        Cursor = Cursors.Hand
    };

    /// <summary>状态条上的下载指示。有正在进行的下载时才出现，点击打开下载面板。</summary>
    private readonly Label _dlLabel = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(0, 0, 4, 0),
        ForeColor = Brand.Blue,
        Font = new Font("Microsoft YaHei UI", 8.25f),
        BackColor = Brand.StatusBg,
        Cursor = Cursors.Hand
    };

    /// <summary>状态条上的更新指示。有可用的新版本才出现，点击查看更新说明并下载。</summary>
    private readonly Label _updateLabel = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(0, 0, 4, 0),
        ForeColor = Color.FromArgb(0x0E, 0xA5, 0x6E),
        Font = new Font("Microsoft YaHei UI", 8.25f),
        BackColor = Brand.StatusBg,
        Cursor = Cursors.Hand
    };

    /// <summary>刚关掉的标签地址，给 Ctrl+Shift+T 用。只留最近 20 个。</summary>
    private readonly List<string> _closedTabs = new();

    /// <summary>用户是否已经对地址栏表达过意图（点了它 / 打了字 / 按了 Ctrl+L）。</summary>
    private bool _suggestArmed;

    private readonly AddressBar _addrBar = new();
    private readonly IconButton _btnBack = new("\uE72B", "后退 (Alt+←)", 12f);
    private readonly IconButton _btnForward = new("\uE72A", "前进 (Alt+→)", 12f);
    private readonly IconButton _btnReload = new("\uE72C", "刷新 (F5)", 11.5f);
    private readonly IconButton _btnHome = new("\uE80F", "新标签页 (Ctrl+T)", 11f);
    private readonly IconButton _btnMenu = new("\uE712", "菜单", 11f);
    private readonly PillButton _btnTl = new("tlstudio.cn", Brand.Blue, true);
    private readonly PillButton _btnDoubler = new("tldoublerstudio.cn", Brand.Purple, true);
    private readonly PillButton _btnDual = new("双站对照", Brand.Blue, false);

    private readonly List<WebTab> _tabs = new();
    private WebTab? _current;
    private CoreWebView2Environment? _env;
    private readonly SuggestPopup _suggest;

    private readonly string? _startUrl;
    private readonly bool _startDual;
    private readonly bool _openGuard;

    /// <summary>最近一次更新检查的结果。非 null 且 HasUpdate 才说明有新版。</summary>
    private UpdateInfo? _updateInfo;

    private bool _fullScreen;
    private FormBorderStyle _savedBorder;
    private FormWindowState _savedState;

    public BrowserForm(string? startUrl = null, bool startDual = false, bool openGuard = false)
    {
        _startUrl = startUrl;
        _startDual = startDual;
        _openGuard = openGuard;
        Text = Brand.AppName;
        BackColor = Brand.ChromeBg;
        MinimumSize = new Size(1040, 640);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1440, 900);
        Font = new Font("Microsoft YaHei UI", 9f);
        KeyPreview = true;
        DoubleBuffered = true;

        // 联想下拉要先于任何 Show 建立，并且由本窗口所有（这样关窗它一定跟着走）
        _suggest = new SuggestPopup(this);
        _suggest.Chosen += OnSuggestionChosen;

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico is not null) Icon = ico;
            }
        }
        catch { }

        BuildChrome();

        _statusBar.Controls.Add(_statusText);
        _statusBar.Controls.Add(_zoomLabel);
        _statusBar.Controls.Add(_blockLabel);
        _statusBar.Controls.Add(_dlLabel);
        _statusBar.Controls.Add(_updateLabel);
        _statusBar.Controls.Add(_shield);
        _shield.Click += (_, _) => ShowGuard();
        _blockLabel.Click += (_, _) => ShowBlockReport();
        _dlLabel.Click += (_, _) => ShowDownloads();
        _updateLabel.Click += (_, _) => UpdateLabelClicked();
        _statusBar.Resize += (_, _) => LayoutStatusBar();

        Controls.Add(_host);
        Controls.Add(_statusBar);
        Controls.Add(_chrome);

        _tabStrip.SelectRequested += tab => SelectTab(tab, focusContent: true);
        _tabStrip.CloseRequested += CloseTab;
        _tabStrip.NewTabRequested += () => _ = OpenTabAsync(null);
        _tabStrip.ContextRequested += ShowTabContextMenu;
        _tabStrip.OrderChanged += ApplyTabOrder;

        _btnBack.Click += (_, _) => _current?.GoBack();
        _btnForward.Click += (_, _) => _current?.GoForward();
        _btnReload.Click += (_, _) => _current?.ReloadOrStop();
        _btnHome.Click += (_, _) => HomeOrNewTab();
        _btnMenu.Click += (_, _) => ShowMainMenu();
        _btnTl.Click += (_, _) => OpenSite(Brand.SiteTl);
        _btnDoubler.Click += (_, _) => OpenSite(Brand.SiteDoubler);
        _btnDual.Click += (_, _) => OpenDualTab();

        _addrBar.Input.KeyDown += AddrKeyDown;

        // 下载进度变化就刷新状态条上的「下载中 N 项」指示
        DownloadStore.Changed += UpdateDownloadLabel;

        // 更新通知的状态先读进来，后台检查会在窗口 Shown 之后触发
        UpdateCheck.LoadState();

        // 启动时地址栏会被程序自动聚焦一次，那时不该弹下拉——什么都没做就先糊一屏建议很吵。
        // 所以攒一个「武装」标记：用户真的点了地址栏、开始打字，或者按了 Ctrl+L 才允许弹。
        _addrBar.Input.TextChanged += (_, _) => { if (_addrBar.Input.Focused) _suggestArmed = true; RefreshSuggest(); };
        _addrBar.UserClicked += () => _suggestArmed = true;
        _addrBar.Input.GotFocus += (_, _) => RefreshSuggest();
        _addrBar.Input.LostFocus += (_, _) => _suggest.Dismiss();

        // 窗口一动下拉的位置就过期了，直接收起来比跟着飞过去更省事也更不容易出错
        Resize += (_, _) => { _suggest.Dismiss(); LayoutChrome(); };
        Move += (_, _) => _suggest.Dismiss();
        Deactivate += (_, _) => _suggest.Dismiss();
        Shown += OnShown;
    }

    private void BuildChrome()
    {
        _toolbar.Controls.Add(_btnBack);
        _toolbar.Controls.Add(_btnForward);
        _toolbar.Controls.Add(_btnReload);
        _toolbar.Controls.Add(_btnHome);
        _toolbar.Controls.Add(_btnMenu);
        _toolbar.Controls.Add(_btnTl);
        _toolbar.Controls.Add(_btnDoubler);
        _toolbar.Controls.Add(_btnDual);
        _toolbar.Controls.Add(_addrBar);

        _chrome.Controls.Add(_toolbar);
        _chrome.Controls.Add(_tabStrip);
    }

    private void LayoutChrome()
    {
        if (_toolbar.ClientSize.Width <= 0) return;

        const int h = 36, pad = 8;
        var y = (44 - h) / 2;
        var x = pad;

        foreach (var b in new Control[] { _btnBack, _btnForward, _btnReload, _btnHome })
        {
            b.SetBounds(x, y, 36, h);
            x += 36;
        }
        x += 6;

        var right = _toolbar.ClientSize.Width - pad;
        _btnMenu.SetBounds(right - 40, y, 40, h);
        right -= 48;

        // 两个网址按钮做成药丸；tldoublerstudio.cn 名字长，单独给宽度
        var wDoubler = 146;
        right -= wDoubler; _btnDoubler.SetBounds(right, y, wDoubler, h);
        right -= 6;
        var wTl = 108;
        right -= wTl; _btnTl.SetBounds(right, y, wTl, h);
        right -= 8;

        var w2 = 92;
        right -= w2; _btnDual.SetBounds(right, y, w2, h);
        right -= 10;

        var addrWidth = Math.Max(140, right - x);
        _addrBar.SetBounds(x, y, addrWidth, h);

        LayoutStatusBar();
    }

    private void LayoutStatusBar()
    {
        if (_statusBar.ClientSize.Width <= 0) return;

        const int shieldW = 128, zoomW = 54, blockW = 138, dlW = 138, updateW = 128, pad = 10;
        var h = _statusBar.ClientSize.Height;
        var x = _statusBar.ClientSize.Width;

        x -= pad; x -= shieldW; _shield.SetBounds(x, 0, shieldW, h);
        x -= 2;   x -= zoomW;   _zoomLabel.SetBounds(x, 0, zoomW, h);
        x -= 2;   x -= blockW;  _blockLabel.SetBounds(x, 0, blockW, h);
        x -= 2;   x -= dlW;     _dlLabel.SetBounds(x, 0, dlW, h);
        x -= 2;   x -= updateW; _updateLabel.SetBounds(x, 0, updateW, h);

        _statusText.SetBounds(0, 0, Math.Max(40, x - 4), h);
    }

    // ────────────────────────────── 启动 ──────────────────────────────

    private async void OnShown(object? sender, EventArgs e)
    {
        // 守护面板和网页内核没关系，先把它顶出来。
        // 不要挪到下面 await 之后：WebView2 刚被上一个实例占过（比如进程刚被杀掉），
        // EnsureCoreWebView2Async 可能卡很久，那样这个面板就永远弹不出来。
        if (_openGuard) BeginInvoke(ShowGuard);

        try
        {
            _env = await CoreWebView2Environment.CreateAsync(null, Brand.UserDataDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "网页组件初始化失败：\n\n" + ex.Message +
                "\n\n请先安装 WebView2 网页组件后重试。",
                Brand.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        try
        {
            await OpenTabAsync(_startUrl, dual: _startDual);
        }
        catch (Exception ex)
        {
            _statusText.Text = "标签页初始化失败：" + ex.Message;
            MessageBox.Show(this, "标签页初始化失败：\n\n" + ex.Message, Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        LayoutChrome();
        UpdateShield();
        _addrBar.FocusInput();

        // 更新检查放最后、异步跑，绝不挡启动；断网或没新版本都静默
        CheckForUpdates(manual: false);
    }

    // ────────────────────────────── 标签管理 ──────────────────────────────

    private async Task<WebTab?> OpenTabAsync(string? url, bool dual = false)
    {
        if (_env is null) return null;

        WebTab tab = dual ? new DualTab() : new PageTab();
        tab.Dock = DockStyle.Fill;
        tab.Visible = false;
        tab.KeyRouter = RouteKey;
        tab.Changed += OnTabChanged;
        tab.NewTabRequested += (sender, uri) => { _ = OpenTabAsync(uri); };

        _host.Controls.Add(tab);
        _tabs.Add(tab);
        _tabStrip.Add(tab);

        SelectTab(tab, focusContent: false);

        await tab.InitAsync(_env, goHome: string.IsNullOrEmpty(url));

        if (!string.IsNullOrEmpty(url)) tab.Navigate(url);

        UpdateChrome();
        if (!dual) tab.FocusContent();
        return tab;
    }

    private void SelectTab(WebTab tab, bool focusContent)
    {
        if (!_tabs.Contains(tab)) return;

        _current = tab;
        foreach (var t in _tabs) t.Visible = ReferenceEquals(t, tab);
        tab.BringToFront();
        _tabStrip.Selected = tab;
        UpdateChrome();

        if (focusContent) tab.FocusContent();
    }

    private void CloseTab(WebTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        var wasCurrent = ReferenceEquals(_current, tab);

        // 记下地址供 Ctrl+Shift+T 恢复。本地新标签页没意义，不记。
        var url = tab.CurrentUrl;
        if (!string.IsNullOrEmpty(url) &&
            (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            _closedTabs.Add(url);
            while (_closedTabs.Count > 20) _closedTabs.RemoveAt(0);
        }

        _tabs.RemoveAt(index);
        _tabStrip.Remove(tab);
        _host.Controls.Remove(tab);

        try { tab.Dispose(); } catch { }

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        if (wasCurrent)
            SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)], focusContent: true);
        else
            UpdateChrome();
    }

    private void CloseOtherTabs(WebTab keep)
    {
        foreach (var t in _tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
            CloseTab(t);
        SelectTab(keep, focusContent: false);
    }

    private void CloseTabsToRight(WebTab from)
    {
        var i = _tabs.IndexOf(from);
        if (i < 0) return;
        foreach (var t in _tabs.Skip(i + 1).ToList()) CloseTab(t);
    }

    private void CycleTab(int delta)
    {
        if (_tabs.Count < 2 || _current is null) return;
        var i = _tabs.IndexOf(_current);
        if (i < 0) return;
        var next = (i + delta + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[next], focusContent: true);
    }

    private void OnTabChanged(WebTab tab)
    {
        _tabStrip.Sync();
        if (ReferenceEquals(tab, _current)) UpdateChrome();
    }

    private void UpdateChrome()
    {
        UpdateShield();

        if (_current is null)
        {
            _btnBack.Enabled = _btnForward.Enabled = false;
            return;
        }

        _btnBack.Enabled = _current.CanGoBack;
        _btnForward.Enabled = _current.CanGoForward;
        _btnReload.Glyph = _current.IsLoading ? "\uE711" : "\uE72C";

        if (!_addrBar.Input.Focused)
        {
            var url = _current.Address;
            _addrBar.Input.Text = _current.IsHomePage || string.IsNullOrEmpty(url) ? "" : url;
            _addrBar.Input.SelectionStart = _addrBar.Input.TextLength;
        }

        var isHttps = _current.Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        _addrBar.Glyph = string.IsNullOrEmpty(_current.Address) || _current.IsHomePage
            ? "\uE774"
            : isHttps ? "\uE72E" : "\uE785";
        _addrBar.GlyphColor = isHttps ? Brand.Blue : Brand.TextDim;

        _statusBar.Busy = _current.IsLoading;
        _statusText.Text = _current.StatusText;
        _zoomLabel.Text = Math.Abs(_current.ZoomFactor - 1.0) < 0.005
            ? ""
            : $"{_current.ZoomFactor * 100:F0}%";
    }

    /// <summary>状态条右边的盾牌：把主页守护的结果显出来，点开看详情。</summary>
    private void UpdateShield()
    {
        var handled = HomeGuard.HandledCount;
        var attention = HomeGuard.AttentionCount;
        _shield.SetState(handled, attention);

        var lines = new List<string>
        {
            "首页与搜索引擎写在程序里，外部程序改不动。"
        };
        lines.Add(handled > 0
            ? $"本次启动已自动拦下并还原 {handled} 项对首页/搜索的改动。"
            : "本次启动没有发现任何改动尝试。");
        if (attention > 0)
            lines.Add($"系统层面有 {attention} 项需要注意（IE 主页 / 启动项 / 360 残留等）。");
        lines.Add(Safety.AdBlock
            ? $"广告与跟踪拦截已开启，本次拦下 {Safety.BlockedTotal} 条。"
            : "广告与跟踪拦截当前是关闭的。");
        lines.Add("点击查看完整守护面板。");
        _shield.Tip = "主页守护：" + string.Join("\n", lines);

        UpdateBlockLabel();
    }

    /// <summary>
    /// 广告拦截计数条。这个数字是「本浏览器没广告」唯一看得见的证据，
    /// 所以只在真的拦到东西时才出现，别摆一个常年 0 的装饰。
    /// </summary>
    private void UpdateBlockLabel()
    {
        var n = Safety.BlockedTotal;
        if (n <= 0)
        {
            _blockLabel.Text = "";
            return;
        }

        var text = $"已拦 {n} 条广告";
        if (Safety.PopupBlocked > 0) text += $" · 弹窗 {Safety.PopupBlocked}";

        if (_blockLabel.Text == text) return;
        _blockLabel.Text = text;
        _blockLabel.ForeColor = Color.FromArgb(0x0F, 0x6E, 0x56);
    }

    private void ShowBlockReport()
    {
        var blocked = Safety.RecentBlocked(30);
        var denied = Safety.RecentDenied(20);

        var sb = new StringBuilder();
        sb.AppendLine($"本次启动以来：拦下 {Safety.BlockedTotal} 条广告/跟踪请求，" +
                      $"拦下弹窗 {Safety.PopupBlocked} 个，");
        sb.AppendLine($"拒绝权限请求 {Safety.DeniedTotal} 次，外部调用询问 {Safety.ExternalAsked} 次。");
        sb.AppendLine($"内置规则 {Safety.RuleCount} 条，拦截开关：{(Safety.AdBlock ? "开" : "关")}。");
        sb.AppendLine();

        sb.AppendLine("—— 最近拦下的（最多 30 条）——");
        if (blocked.Count == 0) sb.AppendLine("  （这次还没有拦到东西）");
        foreach (var b in blocked) sb.AppendLine("  " + b);

        if (denied.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("—— 被拒绝的权限请求 ——");
            foreach (var d in denied) sb.AppendLine("  " + d);
        }

        sb.AppendLine();
        sb.AppendLine("被误拦的站点可以写进这个文件放行（前缀 +），改完重启生效：");
        sb.AppendLine(Safety.UserRulesPath);

        MessageBox.Show(this, sb.ToString(), Brand.AppName + " · 拦截记录",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowGuard()
    {
        HomeGuard.ScanSystem();
        using var guard = new GuardForm();
        guard.ShowDialog(this);
        UpdateShield();
    }

    /// <summary>打开下载面板。常驻单例，避免每次点都新建一个窗口叠起来。</summary>
    private DownloadForm? _downloadForm;

    private void ShowDownloads()
    {
        if (_downloadForm is null || _downloadForm.IsDisposed)
            _downloadForm = new DownloadForm();
        _downloadForm.Show();
        _downloadForm.Activate();
    }

    /// <summary>状态条右侧的下载指示：有正在下的就显示「下载中 N 项」，点开进面板。</summary>
    private void UpdateDownloadLabel()
    {
        if (IsDisposed) return;
        // WebView2 的下载进度事件可能从别的线程来，Label 只能在 UI 线程动
        if (InvokeRequired) { try { BeginInvoke(UpdateDownloadLabel); } catch { } return; }
        var n = DownloadStore.ActiveCount;
        _dlLabel.Text = n > 0 ? $"下载中 {n} 项" : "";
    }

    // ────────────────────────────── 更新检查 ──────────────────────────────

    /// <summary>
    /// 从 GitHub 拉 version.json 比对版本。manual=true 是用户手动点的，要给出明确反馈；
    /// false 是启动后台检查，只在确实有新版时弹一次通知，其余静默。
    /// </summary>
    private async void CheckForUpdates(bool manual)
    {
        if (manual) _statusText.Text = "正在检查更新…";

        var info = await UpdateCheck.CheckAsync();
        if (info is null)
        {
            if (manual) _statusText.Text = "检查更新失败（网络不可用），请稍后再试。";
            return;
        }

        if (!info.HasUpdate)
        {
            _updateLabel.Text = "";
            _updateInfo = null;
            if (manual) _statusText.Text = $"已是最新版本（v{Brand.AppVersion}）。";
            return;
        }

        _updateInfo = info;
        _updateLabel.Text = $"有新版本 v{info.LatestVersion}";

        // 有新版就弹公告（用户要求每次启动都提示）
        ShowUpdateDialog(info);
    }

    /// <summary>点状态条上的「有新版本」：有结果就弹详情，没有就手动查一次。</summary>
    private void UpdateLabelClicked()
    {
        if (_updateInfo is { HasUpdate: true }) ShowUpdateDialog(_updateInfo);
        else CheckForUpdates(manual: true);
    }

    /// <summary>菜单里那一行。有新版时直接显示版本号，让人一眼能看到。</summary>
    private string UpdateMenuText() =>
        _updateInfo is { HasUpdate: true }
            ? $"有新版本 v{_updateInfo.LatestVersion}，点此下载"
            : "检查更新";

    private void UpdateMenuAction()
    {
        if (_updateInfo is { HasUpdate: true }) ShowUpdateDialog(_updateInfo);
        else CheckForUpdates(manual: true);
    }

    private void ShowUpdateDialog(UpdateInfo info)
    {
        var msg = $"发现新版本 v{info.LatestVersion}（当前 v{Brand.AppVersion}）。\n\n";
        if (!string.IsNullOrWhiteSpace(info.Notes)) msg += info.Notes + "\n\n";
        msg += "是否现在打开下载页？";

        var answer = MessageBox.Show(this, msg, Brand.AppName + " · 发现新版本",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) OpenUpdateUrl(info);
    }

    private static void OpenUpdateUrl(UpdateInfo info)
    {
        // 点「打开下载页」统一跳官网下载页（官网里是蓝奏云 + GitHub 双下载）
        _ = info;
        try { Process.Start(new ProcessStartInfo(Brand.UpdatePageUrl) { UseShellExecute = true }); }
        catch { }
    }

    // ────────────────────────────── 入口 ──────────────────────────────

    private void HomeOrNewTab()
    {
        var tab = _current;
        if (tab is PageTab && tab.IsHomePage)
        {
            tab.GoHome();
            return;
        }
        _ = OpenTabAsync(null);
    }

    private void OpenSite(string url)
    {
        var tab = _current;
        if (tab is PageTab && tab.IsHomePage)
        {
            tab.Navigate(url);
            return;
        }
        if (tab is PageTab && string.Equals(tab.CurrentUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            tab.ReloadOrStop();
            return;
        }
        _ = OpenTabAsync(url);
    }

    private void OpenDualTab() => _ = OpenTabAsync(null, dual: true);

    private void AddrKeyDown(object? sender, KeyEventArgs e)
    {
        // 下拉展开时，上下键归它，别让光标在文本框里跑
        if (_suggest.Visible && !e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    _suggest.MoveSelection(+1); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Up:
                    _suggest.MoveSelection(-1); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Enter:
                    _suggest.Commit(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Escape:
                    _suggest.Dismiss(); e.Handled = e.SuppressKeyPress = true; return;
            }
        }

        // Tab 补全域名：只补两个官网，猜不中就什么都不做
        if (e.KeyCode == Keys.Tab && !e.Control && !e.Shift)
        {
            var completed = Brand.CompleteHost(_addrBar.Input.Text);
            if (completed is not null)
            {
                SetAddressText(completed);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
        }

        if (e.KeyCode == Keys.Enter)
        {
            var text = _addrBar.Input.Text.Trim();
            if (text.Length == 0) return;
            _suggest.Dismiss();
            var url = Brand.NormalizeInput(text);
            if (e.Control) _ = OpenTabAsync(url);
            else
            {
                _current?.Navigate(url);
                _current?.FocusContent();
            }
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _suggest.Dismiss();
            UpdateChrome();
            _current?.FocusContent();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    // ────────────────────────────── 地址栏联想 ──────────────────────────────

    private void SetAddressText(string text)
    {
        _addrBar.Input.Text = text;
        _addrBar.Input.SelectionStart = _addrBar.Input.TextLength;
    }

    private void RefreshSuggest()
    {
        if (!_suggestArmed || !_addrBar.Input.Focused) { _suggest.Dismiss(); return; }

        var rows = BuildSuggestions(_addrBar.Input.Text);
        if (rows.Count == 0) { _suggest.Dismiss(); return; }

        _suggest.ShowRows(rows, _addrBar.RectangleToScreen(_addrBar.ClientRectangle), _addrBar.Width);
    }

    /// <summary>
    /// 四类建议，按「越可能想要的越靠上」排：
    /// 直接访问 → 官网直达 → 历史记录 → 搜索。
    /// 重复的地址只留第一条，免得同一个东西出现两遍。
    /// </summary>
    private List<SuggestRow> BuildSuggestions(string input)
    {
        var q = input.Trim();
        var rows = new List<SuggestRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(SuggestRow r)
        {
            if (r.Value.Length == 0 || !seen.Add(r.Value)) return;
            rows.Add(r);
        }

        if (q.Length == 0)
        {
            foreach (var host in Brand.BrandHosts)
                Add(new SuggestRow
                {
                    Kind = SuggestKind.Brand,
                    Text = host,
                    Hint = "官网",
                    Value = "https://" + host
                });

            foreach (var h in HistoryStore.Recent(6))
                Add(new SuggestRow
                {
                    Kind = SuggestKind.History,
                    Text = HistoryLabel(h),
                    Hint = Shorten(h.Url),
                    Value = h.Url
                });

            return rows;
        }

        if (Brand.IsAddressLike(q))
            Add(new SuggestRow
            {
                Kind = SuggestKind.Navigate,
                Text = q,
                Hint = "直接访问",
                Value = Brand.NormalizeInput(q)
            });

        foreach (var host in Brand.BrandHosts)
        {
            if (!host.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !q.Contains(host, StringComparison.OrdinalIgnoreCase)) continue;
            Add(new SuggestRow
            {
                Kind = SuggestKind.Brand,
                Text = host,
                Hint = "官网",
                Value = "https://" + host
            });
        }

        foreach (var h in HistoryStore.Match(q, 6))
            Add(new SuggestRow
            {
                Kind = SuggestKind.History,
                Text = HistoryLabel(h),
                Hint = Shorten(h.Url),
                Value = h.Url
            });

        Add(new SuggestRow
        {
            Kind = SuggestKind.Search,
            Text = q,
            Hint = "搜索",
            Value = Brand.SearchTemplate + Uri.EscapeDataString(q)
        });

        return rows.Take(8).ToList();
    }

    private static string HistoryLabel(HistoryItem h) =>
        string.IsNullOrWhiteSpace(h.Title) ? Shorten(h.Url) : h.Title;

    private static string Shorten(string url)
    {
        var s = url;
        foreach (var p in new[] { "https://", "http://" })
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s[p.Length..]; break; }
        if (s.EndsWith("/", StringComparison.Ordinal)) s = s[..^1];
        return s.Length > 90 ? s[..87] + "..." : s;
    }

    private void OnSuggestionChosen(SuggestRow row)
    {
        _current?.Navigate(row.Value);
        _current?.FocusContent();
    }

    // ────────────────────────────── 标签顺序 ──────────────────────────────

    /// <summary>
    /// 标签条自己先把顺序改好了，这里只把主窗口那份列表同步过来。
    /// 不这么做的话 Ctrl+Tab 和「关闭右侧」还会按老顺序走，拖完就错位。
    /// </summary>
    private void ApplyTabOrder(IReadOnlyList<WebTab> order)
    {
        _tabs.Clear();
        _tabs.AddRange(order);
    }

    // ────────────────────────────── 快捷键 ──────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (RouteKey((uint)(keyData & Keys.KeyCode))) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool RouteKey(uint vk)
    {
        var mods = ModifierKeys;
        var ctrl = (mods & Keys.Control) == Keys.Control;
        var shift = (mods & Keys.Shift) == Keys.Shift;

        switch (vk)
        {
            // 带 Shift 的必须排在只有 Ctrl 的前面，否则会被前面那条先命中
            case 0x54 when ctrl && shift: ReopenClosedTab(); return true;            // Ctrl+Shift+T
            case 0x54 when ctrl: _ = OpenTabAsync(null); return true;                 // Ctrl+T
            case 0x44 when ctrl: ToggleBookmark(); return true;                       // Ctrl+D
            case 0x4A when ctrl: ShowDownloads(); return true;                         // Ctrl+J
            case 0x57 when ctrl: if (_current is not null) CloseTab(_current); return true; // Ctrl+W
            case 0x4C when ctrl:                                                      // Ctrl+L
                _suggestArmed = true;
                _addrBar.FocusInput();
                RefreshSuggest();
                return true;
            case 0x09 when ctrl: CycleTab(shift ? -1 : 1); return true;               // Ctrl+Tab
            case 0x31 when ctrl: OpenSite(Brand.SiteTl); return true;                 // Ctrl+1
            case 0x32 when ctrl: OpenSite(Brand.SiteDoubler); return true;            // Ctrl+2
            case 0x33 when ctrl: OpenDualTab(); return true;                          // Ctrl+3
            case 0x44 when ctrl && shift: OpenDualTab(); return true;                 // Ctrl+Shift+D
            case 0x74: _current?.ReloadOrStop(); return true;                         // F5
            case 0x52 when ctrl: _current?.ReloadOrStop(); return true;               // Ctrl+R
            case 0x7A: ToggleFullScreen(); return true;                               // F11
            case 0x7B: _current?.OpenDevTools(); return true;                         // F12
            case 0x30 when ctrl: _current?.ResetZoom(); return true;                  // Ctrl+0
            case 0xBB or 0x6B when ctrl: ZoomBy(+0.1); return true;                   // Ctrl +/+
            case 0xBD or 0x6D when ctrl: ZoomBy(-0.1); return true;                   // Ctrl -/-
            case 0x50 when ctrl: PrintPage(); return true;                            // Ctrl+P
            case 0x1B: _current?.Stop(); return true;                                 // Esc
        }
        return false;
    }

    private void ZoomBy(double d)
    {
        if (_current is null) return;
        _current.SetZoom(_current.ZoomFactor + d);
    }

    private void PrintPage() => _current?.Print();

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _savedBorder = FormBorderStyle;
            _savedState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = _savedBorder;
            WindowState = _savedState;
        }
        _fullScreen = !_fullScreen;
        LayoutChrome();
    }

    // ────────────────────────────── 菜单 ──────────────────────────────

    private void ShowMainMenu()
    {
        var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9f) };

        menu.Items.Add(Menu("新建标签页", "Ctrl+T", () => _ = OpenTabAsync(null)));
        menu.Items.Add(Menu("双站对照（并排看两个官网）", "Ctrl+Shift+D", OpenDualTab));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("打开 tlstudio.cn", "Ctrl+1", () => OpenSite(Brand.SiteTl)));
        menu.Items.Add(Menu("打开 tldoublerstudio.cn", "Ctrl+2", () => OpenSite(Brand.SiteDoubler)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("恢复刚关闭的标签页", "Ctrl+Shift+T", ReopenClosedTab));
        menu.Items.Add(Menu(DownloadsMenuText(), "Ctrl+J", ShowDownloads));
        menu.Items.Add(BuildBookmarkMenu());
        menu.Items.Add(Menu(_current?.IsMuted == true ? "取消静音此标签" : "静音此标签",
            null, () => _current?.ToggleMute()));
        menu.Items.Add(AdBlockMenuItem());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("复制当前网址", "Ctrl+Shift+C", CopyUrl));
        menu.Items.Add(Menu("在系统默认浏览器中打开", null, OpenInDefaultBrowser));
        menu.Items.Add(Menu("清除浏览记录（" + HistoryStore.Count + " 条）", null, ClearHistory));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("放大", "Ctrl++", () => ZoomBy(+0.1)));
        menu.Items.Add(Menu("缩小", "Ctrl+-", () => ZoomBy(-0.1)));
        menu.Items.Add(Menu("重置缩放", "Ctrl+0", () => _current?.ResetZoom()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("开发者工具", "F12", () => _current?.OpenDevTools()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu(GuardMenuText(), null, ShowGuard));
        menu.Items.Add(Menu(UpdateMenuText(), null, UpdateMenuAction));
        menu.Items.Add(Menu("关于 TL 浏览器", null, ShowAbout));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("退出", null, Close));

        menu.Show(_btnMenu, new Point(0, _btnMenu.Height + 2));
    }

    private static string GuardMenuText()
    {
        var handled = HomeGuard.HandledCount;
        if (handled > 0) return $"主页守护（已拦下 {handled} 项）";

        var attention = HomeGuard.AttentionCount;
        return attention > 0 ? $"主页守护（{attention} 项待看）" : "主页守护";
    }

    private static string DownloadsMenuText()
    {
        var n = DownloadStore.ActiveCount;
        return n > 0 ? $"下载（进行中 {n} 项）" : "下载";
    }

    private static ToolStripMenuItem Menu(string text, string? shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut is not null) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// 广告拦截开关。做成菜单里能一眼看出当前状态的开关项，
    /// 而不是藏在设置深里——被误拦时用户得能立刻关掉，不然就只能弃用。
    /// </summary>
    private ToolStripMenuItem AdBlockMenuItem()
    {
        var item = new ToolStripMenuItem(
            Safety.AdBlock
                ? $"广告与跟踪拦截：已开启（已拦 {Safety.BlockedTotal} 条）"
                : "广告与跟踪拦截：已关闭")
        {
            Checked = Safety.AdBlock
        };
        item.Click += (_, _) =>
        {
            Safety.AdBlock = !Safety.AdBlock;
            UpdateShield();
            _statusText.Text = Safety.AdBlock ? "广告与跟踪拦截已开启。" : "广告与跟踪拦截已关闭。";
        };
        return item;
    }

    private ToolStripMenuItem BuildBookmarkMenu()
    {
        var root = new ToolStripMenuItem("书签");
        var url = _current?.CurrentUrl ?? "";
        var marked = Bookmarks.Contains(url);

        root.DropDownItems.Add(Menu(marked ? "取消收藏此页" : "收藏此页", "Ctrl+D", ToggleBookmark));

        var list = Bookmarks.All();
        if (list.Count > 0)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            foreach (var b in list.Take(25))
            {
                var target = b.Url;
                var text = string.IsNullOrWhiteSpace(b.Title) ? b.Url : b.Title;
                if (text.Length > 60) text = text[..57] + "...";
                var item = new ToolStripMenuItem(text) { ToolTipText = b.Url };
                item.Click += (_, _) => OpenSite(target);
                root.DropDownItems.Add(item);
            }
        }

        return root;
    }

    private void ToggleBookmark()
    {
        var tab = _current;
        if (tab is null) return;

        var url = tab.CurrentUrl;
        if (string.IsNullOrEmpty(url) ||
            url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            _statusText.Text = "本地新标签页不用收藏。";
            return;
        }

        var added = Bookmarks.Toggle(url, tab.Title);
        _statusText.Text = added ? "已收藏：" + tab.Title : "已取消收藏：" + tab.Title;
    }

    /// <summary>Ctrl+Shift+T：把刚关掉的那个标签原样开回来。</summary>
    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            _statusText.Text = "没有刚关闭的标签页可以恢复。";
            return;
        }

        var url = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        _ = OpenTabAsync(url);
    }

    private void ShowTabContextMenu(WebTab tab)
    {
        var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9f) };
        menu.Items.Add(Menu("刷新", "F5", () =>
        {
            SelectTab(tab, false);
            tab.ReloadOrStop();
        }));
        menu.Items.Add(Menu("复制网址", null, () =>
        {
            try { Clipboard.SetText(tab.CurrentUrl); } catch { }
        }));
        menu.Items.Add(Menu(tab.IsMuted ? "取消静音" : "静音此标签", null, () =>
        {
            SelectTab(tab, false);
            tab.ToggleMute();
        }));
        menu.Items.Add(Menu(Bookmarks.Contains(tab.CurrentUrl) ? "取消收藏" : "收藏此标签", null, () =>
        {
            SelectTab(tab, false);
            ToggleBookmark();
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menu("关闭标签页", "Ctrl+W", () => CloseTab(tab)));
        menu.Items.Add(Menu("关闭其他标签页", null, () => CloseOtherTabs(tab)));
        menu.Items.Add(Menu("关闭右侧标签页", null, () => CloseTabsToRight(tab)));
        if (tab is DualTab dual)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Menu("左右互换", null, dual.SwapSides));
        }
        menu.Show(Cursor.Position);
    }

    private void CopyUrl()
    {
        try
        {
            var url = _current?.CurrentUrl;
            if (!string.IsNullOrEmpty(url)) Clipboard.SetText(url);
        }
        catch { }
    }

    private void OpenInDefaultBrowser()
    {
        try
        {
            var url = _current?.CurrentUrl;
            if (string.IsNullOrEmpty(url) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                url = Brand.SiteTl;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void ClearHistory()
    {
        var n = HistoryStore.Count;
        if (n == 0) return;

        var answer = MessageBox.Show(this,
            $"确定清除本浏览器的全部浏览记录吗？\n\n共 {n} 条。\n" +
            "只会删除本程序自己记录的历史，不影响其他浏览器。",
            Brand.AppName + " · 清除浏览记录",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        HistoryStore.Clear();
        RefreshSuggest();
    }

    private void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { _suggest.Dismiss(); _suggest.Dispose(); } catch { }

        if (_env is not null && _tabs.Count > 0)
        {
            foreach (var t in _tabs.ToList())
            {
                try { t.Dispose(); } catch { }
            }
            _tabs.Clear();
        }

        // 历史写盘是节流的，退出前一定要补一次，否则最后几秒的访问会丢
        HistoryStore.Flush();
        base.OnFormClosing(e);
    }
}
