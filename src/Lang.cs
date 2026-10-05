using System.Globalization;
using System.Text;

namespace TLBrowser;

/// <summary>
/// 界面语言。目前两套：简体中文 / English。
/// 设计上刻意做成「一张表 + 一个开关」：所有界面文案都从 T(key) 取，
/// 不在代码里散落硬编码中文，否则加一门语言就得全项目翻一遍。
/// 值是格式串，带参数的用 T("key", n)（占位符 {0} {1}）。
/// </summary>
internal static class Lang
{
    public const string ZH = "zh-CN";
    public const string EN = "en-US";

    /// <summary>[中文, English]</summary>
    private static readonly Dictionary<string, string[]> Pack = new()
    {
        // ── 主菜单 ──
        // ── 标签与网页内提示 ──
        ["tab.newTitle"] = new[] { "新标签页", "New tab" },
        ["tab.dualTitle"] = new[] { "双站对照", "Side-by-side" },
        ["tab.opening"] = new[] { "正在打开 {0}", "Opening {0}" },
        ["tab.loadFailed"] = new[] { "加载失败：{0}", "Failed to load: {0}" },
        ["tab.crashed"] = new[] { "页面进程异常（{0}），按 F5 可重试", "Page process crashed ({0}) — press F5 to retry" },
        ["dl.unknownFile"] = new[] { "(未知文件)", "(unknown file)" },
        ["dl.confirmTitle"] = new[] { "{0} · 下载确认", "{0} · Confirm download" },
        ["dl.started"] = new[] { "已开始下载：{0}", "Downloading: {0}" },
        ["ext.confirmBody"] = new[] {
            "网页请求打开一个外部程序或协议：\n\n{0}\n\n如果不是你自己点的，请选「否」。",
            "The page wants to open an external app or protocol:\n\n{0}\n\nIf you didn't ask for it, choose \"No\"." },
        ["ext.confirmTitle"] = new[] { "{0} · 外部调用确认", "{0} · External app" },

        // ── 启动与安装 ──
        ["boot.splash"] = new[] { "{0} · 启动中", "{0} · Starting" },
        ["boot.skip"] = new[] { "点击任意位置跳过", "Click anywhere to skip" },
        ["boot.noRuntime"] = new[] {
            "缺少 WebView2 网页组件，TL 浏览器需要它才能显示网页。\n\n是否现在打开官方下载页？装完再打开本程序即可。",
            "The WebView2 runtime is missing — TL Browser needs it to display pages.\n\nOpen the official download page now? Install it, then start this app again." },
        ["boot.noRuntimeTitle"] = new[] { "{0} · 缺少网页组件", "{0} · Missing web component" },

        // ── 标签与网页内提示（占位结束）──
        ["menu.newTab"] = new[] { "新建标签页", "New Tab" },
        ["tip.back"] = new[] { "后退 (Alt+←)", "Back (Alt+←)" },
        ["tip.forward"] = new[] { "前进 (Alt+→)", "Forward (Alt+→)" },
        ["tip.reload"] = new[] { "刷新 (F5)", "Reload (F5)" },
        ["tip.home"] = new[] { "新标签页 (Ctrl+T)", "New tab (Ctrl+T)" },
        ["tip.menu"] = new[] { "菜单", "Menu" },
        ["menu.dual"] = new[] { "双站对照（并排看两个官网）", "Side-by-side (both sites)" },
        ["menu.openTl"] = new[] { "打开 tlstudio.cn", "Open tlstudio.cn" },
        ["menu.openDoubler"] = new[] { "打开 tldoublerstudio.cn", "Open tldoublerstudio.cn" },
        ["menu.reopen"] = new[] { "恢复刚关闭的标签页", "Reopen closed tab" },
        ["menu.downloads"] = new[] { "下载", "Downloads" },
        ["menu.downloadsActive"] = new[] { "下载（进行中 {0} 项）", "Downloads ({0} active)" },
        ["menu.bookmarks"] = new[] { "书签", "Bookmarks" },
        ["menu.mute"] = new[] { "静音此标签", "Mute this tab" },
        ["menu.unmute"] = new[] { "取消静音此标签", "Unmute this tab" },
        ["menu.copyUrl"] = new[] { "复制当前网址", "Copy current URL" },
        ["menu.openInSystem"] = new[] { "在系统默认浏览器中打开", "Open in default browser" },
        ["menu.clearHistory"] = new[] { "清除浏览记录（{0} 条）", "Clear history ({0} items)" },
        ["menu.zoomIn"] = new[] { "放大", "Zoom in" },
        ["menu.zoomOut"] = new[] { "缩小", "Zoom out" },
        ["menu.zoomReset"] = new[] { "重置缩放", "Reset zoom" },
        ["menu.devTools"] = new[] { "开发者工具", "Developer tools" },
        ["menu.guard"] = new[] { "主页守护", "Homepage guard" },
        ["menu.guardBlocked"] = new[] { "主页守护（已拦下 {0} 项）", "Homepage guard ({0} blocked)" },
        ["menu.guardAttention"] = new[] { "主页守护（{0} 项待看）", "Homepage guard ({0} to review)" },
        ["menu.checkUpdate"] = new[] { "检查更新", "Check for updates" },
        ["menu.hasUpdate"] = new[] { "有新版本 v{0}，点此下载", "New version v{0} — click to get it" },
        ["menu.about"] = new[] { "关于 TL 浏览器", "About TL Browser" },
        ["menu.exit"] = new[] { "退出", "Exit" },
        ["menu.language"] = new[] { "界面语言", "Interface language" },
        ["menu.langZh"] = new[] { "简体中文", "Chinese (Simplified)" },
        ["menu.langEn"] = new[] { "English", "English" },

        // ── 广告拦截开关 ──
        ["adblock.on"] = new[] { "广告与跟踪拦截：已开启（已拦 {0} 条）", "Ad & tracker blocking: ON ({0} blocked)" },
        ["adblock.off"] = new[] { "广告与跟踪拦截：已关闭", "Ad & tracker blocking: OFF" },
        ["adblock.statusOn"] = new[] { "广告与跟踪拦截已开启。", "Ad & tracker blocking is now ON." },
        ["adblock.statusOff"] = new[] { "广告与跟踪拦截已关闭。", "Ad & tracker blocking is now OFF." },

        // ── 书签 ──
        ["bm.add"] = new[] { "收藏此页", "Bookmark this page" },
        ["bm.remove"] = new[] { "取消收藏此页", "Remove bookmark" },
        ["bm.empty"] = new[] { "（还没有书签）", "(no bookmarks yet)" },
        ["bm.open"] = new[] { "打开", "Open" },
        ["bm.delete"] = new[] { "删除", "Delete" },

        // ── 标签右键菜单 ──
        ["tab.refresh"] = new[] { "刷新", "Reload" },
        ["tab.close"] = new[] { "关闭标签页", "Close tab" },
        ["tab.closeOthers"] = new[] { "关闭其他标签页", "Close other tabs" },
        ["tab.closeRight"] = new[] { "关闭右侧标签页", "Close tabs to the right" },
        ["dual.swap"] = new[] { "左右互换", "Swap sides" },

        // ── 翻译 ──
        ["menu.translatePage"] = new[] { "翻译此页", "Translate this page" },
        ["menu.translateText"] = new[] { "翻译选中的文字", "Translate selection" },
        ["translate.title"] = new[] { "翻译", "Translate" },
        ["status.translating"] = new[] { "已在新标签页打开翻译", "Translation opened in a new tab" },

        // ── 状态条 ──
        ["status.blocked"] = new[] { "已拦 {0} 条广告", "{0} ads blocked" },
        ["status.popups"] = new[] { " · 弹窗 {0}", " · {0} pop-ups" },
        ["status.downloading"] = new[] { "下载中 {0} 项", "Downloading {0}" },
        ["status.newVersion"] = new[] { "有新版本 v{0}", "New version v{0}" },
        ["status.checking"] = new[] { "正在检查更新…", "Checking for updates…" },
        ["status.checkFailed"] = new[] { "检查更新失败（网络不可用），请稍后再试。", "Update check failed (no network). Try later." },
        ["status.upToDate"] = new[] { "已是最新版本（v{0}）。", "Already up to date (v{0})." },
        ["status.loading"] = new[] { "加载中…", "Loading…" },
        ["status.done"] = new[] { "完成", "Done" },
        ["status.copied"] = new[] { "已复制网址", "URL copied" },
        ["status.historyCleared"] = new[] { "浏览记录已清除", "History cleared" },
        ["status.muted"] = new[] { "已静音此标签", "Tab muted" },
        ["status.unmuted"] = new[] { "已取消静音", "Tab unmuted" },
        ["status.initFailed"] = new[] { "标签页初始化失败：{0}", "Tab failed to initialize: {0}" },
        ["status.noClosedTab"] = new[] { "没有刚关闭的标签页可以恢复。", "No recently closed tab to reopen." },
        ["bm.localSkip"] = new[] { "本地新标签页不用收藏。", "No need to bookmark the local new tab." },
        ["bm.added"] = new[] { "已收藏：{0}", "Bookmarked: {0}" },
        ["bm.removed"] = new[] { "已取消收藏：{0}", "Bookmark removed: {0}" },

        // ── 更新弹窗 ──
        ["update.foundTitle"] = new[] { "{0} · 发现新版本", "{0} · New version available" },
        ["update.foundBody"] = new[] { "发现新版本 v{0}（当前 v{1}）。", "Version v{0} is available (you have v{1})." },
        ["update.openAsk"] = new[] { "是否现在打开下载页？", "Open the download page now?" },
        ["update.notesTitle"] = new[] { "{0} · 更新内容", "{0} · What's new" },
        ["common.clearHistoryTitle"] = new[] { "{0} · 清除浏览记录", "{0} · Clear browsing history" },
        ["common.clearHistoryAsk"] = new[] {
            "确定清除本浏览器的全部浏览记录吗？\n\n共 {0} 条。\n只会删除本程序自己记录的历史，不影响其他浏览器。",
            "Clear all browsing history of this browser?\n\n{0} entries.\nOnly this app's own history is deleted — other browsers are unaffected." },
        ["common.adblockLogTitle"] = new[] { "{0} · 拦截记录", "{0} · Blocked list" },

        ["shield.handledShort"] = new[] { "主页守护 · 已拦下 {0}", "Guard · blocked {0}" },
        ["shield.todoShort"] = new[] { "主页守护 · 待看 {0}", "Guard · {0} to review" },
        ["shield.normalShort"] = new[] { "主页守护 · 正常", "Guard · all good" },

        // ── 地址栏联想 ──
        ["sug.brand"] = new[] { "官网", "Official site" },
        ["sug.navigate"] = new[] { "直接访问", "Go to" },
        ["sug.search"] = new[] { "搜索", "Search" },
        ["sug.placeholder"] = new[] { "搜索或输入网址", "Search or type a URL" },

        // ── 新标签页（主页）──
        // 主页是内嵌的 HTML，里面这几段文案靠 %%占位符%% 在写盘时注入，
        // 所以改语言时必须重新落盘一次，否则网页还是上一门语言。
        ["home.lang"] = new[] { "zh-CN", "en" },
        ["home.title"] = new[] { "新标签页", "New tab" },
        ["home.hint"] = new[] { "Ctrl+T 新标签　·　Ctrl+1 / Ctrl+2 直达　·　Ctrl+Shift+D 双站对照",
                                "Ctrl+T New tab　·　Ctrl+1 / Ctrl+2 Jump　·　Ctrl+Shift+D Side-by-side" },
        ["home.tilesLabel"] = new[] { "快捷入口", "Quick links" },
        ["home.searchWith"] = new[] { "搜索", "Search with" },
        ["home.addTile"] = new[] { "添加快捷入口", "Add shortcut" },
        ["home.removeTile"] = new[] { "移除此快捷入口", "Remove this shortcut" },
        ["home.addTitle"] = new[] { "添加快捷入口", "Add shortcut" },
        ["home.nameLabel"] = new[] { "名称", "Name" },
        ["home.urlLabel"] = new[] { "网址", "Address" },
        ["home.emptyName"] = new[] { "网址不能为空。", "Address cannot be empty." },
        ["home.badUrl"] = new[] { "网址看起来不对，请以 http:// 或 https:// 开头。",
                                  "That does not look like a valid address. Use http:// or https://." },
        ["home.removed"] = new[] { "已移除 {0}。", "Removed {0}." },

        // ── 守护盾牌提示 ──
        ["shield.title"] = new[] { "主页守护：", "Homepage guard:" },
        ["shield.hardcoded"] = new[] { "首页与搜索引擎写在程序里，外部程序改不动。",
                                       "Homepage and search engine are hardcoded — no outside program can change them." },
        ["shield.handled"] = new[] { "本次启动已自动拦下并还原 {0} 项对首页/搜索的改动。",
                                     "Auto-blocked and restored {0} homepage/search changes this session." },
        ["shield.none"] = new[] { "本次启动没有发现任何改动尝试。", "No change attempts this session." },
        ["shield.attention"] = new[] { "系统层面有 {0} 项需要注意（IE 主页 / 启动项 / 360 残留等）。",
                                       "{0} system items need attention (IE homepage / startup entries / 360 leftovers)." },
        ["shield.adOn"] = new[] { "广告与跟踪拦截已开启，本次拦下 {0} 条。",
                                  "Ad & tracker blocking is ON — {0} blocked this session." },
        ["shield.adOff"] = new[] { "广告与跟踪拦截当前是关闭的。", "Ad & tracker blocking is currently OFF." },
        ["shield.more"] = new[] { "点击查看完整守护面板。", "Click to open the full guard panel." },

        // ── 拦截记录报告 ──
        ["report.sum1"] = new[] { "本次启动以来：拦下 {0} 条广告/跟踪请求，拦下弹窗 {1} 个，",
                                  "Since startup: {0} ad/tracker requests blocked, {1} pop-ups blocked," },
        ["report.sum2"] = new[] { "拒绝权限请求 {0} 次，外部调用询问 {1} 次。",
                                  "{0} permission requests denied, {1} external-call prompts." },
        ["report.sum3"] = new[] { "内置规则 {0} 条，拦截开关：{1}。",
                                  "{0} built-in rules, blocking: {1}." },
        ["report.on"] = new[] { "开", "ON" },
        ["report.off"] = new[] { "关", "OFF" },
        ["report.recent"] = new[] { "—— 最近拦下的（最多 30 条）——", "— Recently blocked (max 30) —" },
        ["report.recentEmpty"] = new[] { "  （这次还没有拦到东西）", "  (nothing blocked yet)" },
        ["report.denied"] = new[] { "—— 被拒绝的权限请求 ——", "— Denied permission requests —" },
        ["report.allowHint"] = new[] { "被误拦的站点可以写进这个文件放行（前缀 +），改完重启生效：",
                                       "To allow a wrongly blocked site, add it to this file (prefix +), then restart:" },
        ["common.webviewMissing"] = new[] { "网页组件初始化失败：\n\n{0}\n\n请先安装 WebView2 网页组件后重试。",
                                            "The web component failed to start:\n\n{0}\n\nPlease install the WebView2 runtime and try again." },
        ["common.tabInitFailed"] = new[] { "标签页初始化失败：\n\n{0}", "Tab failed to initialize:\n\n{0}" },

        // ── 通用 ──
        ["common.ok"] = new[] { "确定", "OK" },
        ["common.cancel"] = new[] { "取消", "Cancel" },
        ["common.yes"] = new[] { "是", "Yes" },
        ["common.no"] = new[] { "否", "No" },
        ["common.close"] = new[] { "关闭", "Close" },
        ["common.error"] = new[] { "出错了", "Error" },
        ["common.tip"] = new[] { "提示", "Tip" },
        // 新标签页现在是每次导航现场生成的，切语言立刻生效，不用再提示"下次打开才生效"
        ["common.languageSwitched"] = new[] { "界面语言已切换为「{0}」。", "Language switched to \"{0}\"." },

        // ── 下载面板 ──
        ["dl.window"] = new[] { "{0} · 下载", "{0} · Downloads" },
        ["dl.title"] = new[] { "下载", "Downloads" },
        ["dl.active"] = new[] { "正在下载", "Downloading" },
        ["dl.history"] = new[] { "下载记录", "Download history" },
        ["dl.openFile"] = new[] { "打开文件", "Open file" },
        ["dl.openFolder"] = new[] { "打开所在文件夹", "Open folder" },
        ["dl.cancelSel"] = new[] { "取消选中", "Cancel selected" },
        ["dl.delete"] = new[] { "删除记录", "Delete" },
        ["dl.clear"] = new[] { "清空记录", "Clear all" },
        ["dl.close"] = new[] { "关闭", "Close" },
        ["dl.col.file"] = new[] { "文件名", "File" },
        ["dl.col.size"] = new[] { "大小", "Size" },
        ["dl.col.progress"] = new[] { "进度", "Progress" },
        ["dl.col.source"] = new[] { "来源", "Source" },
        ["dl.col.status"] = new[] { "状态", "Status" },
        ["dl.col.time"] = new[] { "时间", "Time" },
        ["dl.sumActive"] = new[] { "正在下载 {0} 项　·　历史记录 {1} 条", "Downloading {0}　·　{1} in history" },
        ["dl.sumIdle"] = new[] { "没有正在进行的下载　·　历史记录 {0} 条", "Nothing downloading　·　{0} in history" },
        ["dl.idleActive"] = new[] { "当前没有正在进行的下载", "Nothing downloading right now" },
        ["dl.idleHistory"] = new[] { "还没有下载记录", "No downloads yet" },
        ["dl.progressUnknown"] = new[] { "{0} 已下载", "{0} downloaded" },
        ["dl.pickHistory"] = new[] { "先在「下载记录」里选中一条。", "Select an entry under \"Download history\" first." },
        ["dl.pickActive"] = new[] { "先在「正在下载」里选中一条进行中的下载。", "Select an active download under \"Downloading\" first." },
        ["dl.missingFile"] = new[] { "文件不存在，可能已被移动或删除。\n\n{0}", "The file no longer exists — it may have been moved or deleted.\n\n{0}" },
        ["dl.clearAsk"] = new[] { "确定清空全部下载记录吗？\n\n只会删除记录列表，已经下好的文件不会被删。",
                                  "Clear the whole download history?\n\nOnly the list is cleared — files you already downloaded stay." },
        ["dl.clearTitle"] = new[] { "{0} · 清空下载记录", "{0} · Clear download history" },

        // ── 守护面板 ──
        ["guard.window"] = new[] { "{0} · 主页守护", "{0} · Homepage guard" },
        ["guard.title"] = new[] { "主页守护", "Homepage guard" },
        ["guard.blocked"] = new[] { "本次拦截与修复", "Blocked & repaired this session" },
        ["guard.system"] = new[] { "系统检查（只读，本程序没有改动它们）", "System check (read-only — nothing was modified)" },
        ["guard.col.time"] = new[] { "时间", "Time" },
        ["guard.col.status"] = new[] { "状态", "Status" },
        ["guard.col.kind"] = new[] { "类型", "Type" },
        ["guard.col.detail"] = new[] { "说明", "Detail" },
        ["guard.col.cat"] = new[] { "类别", "Category" },
        ["guard.col.item"] = new[] { "项目", "Item" },
        ["guard.idle"] = new[] { "本次启动没有发现任何改动尝试", "No change attempts this session" },
        ["guard.statusOk"] = new[] { "正常", "OK" },
        ["guard.sumHandled"] = new[] { "已自动拦下并还原 {0} 项", "Blocked and restored {0} items" },
        ["guard.sumNone"] = new[] { "本次启动没有被改动过", "Nothing was changed this session" },
        ["guard.sumAttention"] = new[] { "系统层面 {0} 项待看", "{0} system items need a look" },
        ["guard.sumClean"] = new[] { "系统层面无异常", "System clean" },
        ["guard.sumPrefix"] = new[] { "首页与搜索引擎写死在程序里，外部改不动",
                                      "Homepage and search engine are hardcoded — nothing outside can change them" },
        ["guard.noLog"] = new[] { "还没有产生过任何记录。", "No log entries yet." },
        ["guard.logFailed"] = new[] { "打不开日志：{0}", "Cannot open the log: {0}" },
        ["guard.openLog"] = new[] { "打开守护日志", "Open guard log" },
        ["guard.fix"] = new[] { "修复主页劫持", "Fix homepage hijack" },
        ["guard.rescan"] = new[] { "重新检查", "Re-check" },
        ["guard.fixTitle"] = new[] { "{0} · 修复主页劫持", "{0} · Fix homepage hijack" },
        ["guard.fixAsk"] = new[] {
            "⚠️ 这一步会改注册表，不会碰 360 的任何程序文件，也不会卸载软件。\n\n" +
            "会做的事：\n" +
            "  1. 先把当前 IE 主页 / 搜索设置备份到一个文本文件\n" +
            "  2. 删除主页锁定值（如果存在）：\n" +
            "       {0} → HomePage / SearchScopes\n" +
            "     这是「主页设置被锁住、自己改不回来」的常见原因\n" +
            "  3. 如果 IE 主页被指向了导航站，改回 https://tlstudio.cn\n" +
            "  4. 如果 IE 搜索页被指向了导航站，改回 https://www.bing.com/search?q=%s\n\n" +
            "不会做的事：\n" +
            "  · 不删除或修改 360 的程序文件\n" +
            "  · 不卸载任何软件\n" +
            "  · 不改动 HKLM（需要管理员权限的那部分）\n" +
            "  · 不会碰 IE 正常的主页设置（只处理被判定为导航站的）\n\n" +
            "要现在执行吗？",
            "⚠️ This edits the Windows registry. It never touches 360's program files and never uninstalls anything.\n\n" +
            "What it will do:\n" +
            "  1. Back up your current IE homepage / search settings to a text file\n" +
            "  2. Delete the homepage lock values (if present):\n" +
            "       {0} → HomePage / SearchScopes\n" +
            "     That lock is the usual reason \"I can't change my homepage back\"\n" +
            "  3. If the IE homepage points at a navigation site, set it back to https://tlstudio.cn\n" +
            "  4. If the IE search page points at a navigation site, set it back to https://www.bing.com/search?q=%s\n\n" +
            "What it will NOT do:\n" +
            "  · No deleting or editing of 360's program files\n" +
            "  · No uninstalling anything\n" +
            "  · No touching HKLM (the part that needs admin rights)\n" +
            "  · No touching a normal IE homepage (only ones detected as navigation sites)\n\n" +
            "Run it now?" },
        ["guard.fixDoneTitle"] = new[] { "{0} · 已完成", "{0} · Done" },
        ["guard.fixDone"] = new[] {
            "{0}\n\n改动前的原始值已备份到：\n{1}\n\n如果 360 还装着「主页防护」之类的常驻功能，" +
            "它可能过一会儿又写回去 —— 那就需要在 360 自己的设置里关掉它。本程序不会去动别的软件。",
            "{0}\n\nThe original values were backed up to:\n{1}\n\nIf 360 still runs something like " +
            "\"Homepage protection\", it may write them back later — turn that off inside 360's own settings. " +
            "This program never touches other software." },

        // ── 关于 ──
        ["about.title"] = new[] { "关于 {0}", "About {0}" },
        ["about.version"] = new[] { "版本 {0}", "Version {0}" },
        ["about.note"] = new[] { "两个官网，一个窗口。\n缓存与登录数据只保存在本机，不上传任何信息。",
                                 "Two official sites, one window.\nCache and sign-in data stay on this PC — nothing is uploaded." },
        ["about.close"] = new[] { "关闭", "Close" },
    };

    /// <summary>当前是不是英文。</summary>
    public static bool IsEn { get; private set; }

    private static string StatePath => Path.Combine(Brand.AppDataDir, "lang.txt");

    public static void LoadState()
    {
        try
        {
            var v = File.ReadAllText(StatePath).Trim();
            IsEn = string.Equals(v, EN, StringComparison.OrdinalIgnoreCase);
        }
        catch { IsEn = false; }
    }

    /// <summary>切换语言并落盘。返回新的显示名，方便提示用户。</summary>
    public static string SetLanguage(string lang)
    {
        IsEn = string.Equals(lang, EN, StringComparison.OrdinalIgnoreCase);
        try { File.WriteAllText(StatePath, IsEn ? EN : ZH, new UTF8Encoding(false)); }
        catch { }
        return IsEn ? "English" : "简体中文";
    }

    public static void Toggle() => SetLanguage(IsEn ? ZH : EN);

    /// <summary>下载状态是落盘数据，语言切了旧记录里的中文还在，所以按原值映射显示。</summary>
    private static readonly Dictionary<string, string> DlStateMap = new()
    {
        ["已完成"] = "Done",
        ["已取消"] = "Canceled",
        ["已中断"] = "Interrupted",
    };

    public static string DlState(string state) =>
        IsEn && DlStateMap.TryGetValue(state, out var v) ? v : state;

    public static bool IsDone(string state) =>
        string.Equals(state, "已完成", StringComparison.Ordinal);

    /// <summary>取文案。args 给值时按格式串处理（占位符 {0}）。</summary>
    public static string T(string key, params object[] args)
    {
        if (!Pack.TryGetValue(key, out var pair)) return key;
        var s = IsEn ? pair[1] : pair[0];
        return args.Length > 0 ? string.Format(CultureInfo.CurrentCulture, s, args) : s;
    }
}
