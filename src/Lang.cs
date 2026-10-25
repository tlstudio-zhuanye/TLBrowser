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
        ["tab.splitTitle"] = new[] { "分屏", "Split view" },
        ["tab.splitClose"] = new[] { "关掉这一侧", "Close this pane" },
        ["split.on"] = new[] { "分屏", "Split" },
        ["split.off"] = new[] { "退出分屏", "Exit split" },
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
        ["menu.splitOn"] = new[] { "分屏（当前页面留左，右边新开一个）", "Split view (current left, new pane right)" },
        ["menu.splitOff"] = new[] { "取消分屏", "Exit split view" },
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
        ["home.hint"] = new[] { "Ctrl+T 新标签　·　Ctrl+1 / Ctrl+2 直达　·　Ctrl+Shift+D 双站对照　·　Ctrl+Shift+S 分屏",
                                "Ctrl+T New tab　·　Ctrl+1 / Ctrl+2 Jump　·　Ctrl+Shift+D Side-by-side　·　Ctrl+Shift+S Split" },
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

        // 下载状态：落盘存的是 key（done/canceled/interrupted），显示时才翻成当前语言
        ["dl.state.done"] = new[] { "已完成", "Completed" },
        ["dl.state.canceled"] = new[] { "已取消", "Canceled" },
        ["dl.state.interrupted"] = new[] { "已中断", "Interrupted" },
        ["dl.state.active"] = new[] { "下载中", "Downloading" },

        // 下载位置
        ["dl.folderPrefix"] = new[] { "保存到：", "Save to: " },
        ["dl.changeFolder"] = new[] { "更改位置…", "Change…" },
        ["dl.resetFolder"] = new[] { "恢复默认", "Default" },
        ["dl.folderPickDesc"] = new[] { "选一个文件夹，之后的下载都会存到这里（已经下好的文件不受影响）。",
                                        "Pick a folder — future downloads go there. Files you already downloaded stay put." },
        ["dl.folderBad"] = new[] { "这个文件夹不可用或无法写入：\n\n{0}", "That folder is unusable or not writable:\n\n{0}" },
        ["dl.confirmBody"] = new[] { "文件名：{0}\n来源：{1}\n\n将保存到：{2}\n\n只下载你自己要的东西。确定下载吗？",
                                     "File: {0}\nFrom: {1}\n\nSaving to: {2}\n\nOnly download what you actually want. Continue?" },

        // ── 企业模式（网站白名单）──
        ["kiosk.menu"] = new[] { "企业模式…", "Enterprise mode…" },
        ["kiosk.window"] = new[] { "{0} · 企业模式", "{0} · Enterprise mode" },
        ["kiosk.title"] = new[] { "企业模式 · 网站白名单", "Enterprise mode · Site allowlist" },
        ["kiosk.stateOff"] = new[] { "当前未启用。开启后浏览器只能访问下面名单里的网站，名单之外的站一律拦住；改名单要管理员密码。",
                                     "Not enabled. Once enabled the browser can only reach the sites listed below — everything else is blocked, and changing the list needs the admin password." },
        ["kiosk.stateOn"] = new[] { "已启用 · 允许访问 {0} 个网站", "On · {0} sites allowed" },
        ["kiosk.listLabel"] = new[] { "允许访问的网站（第一条作为启动页）", "Allowed sites (the first one is the start page)" },
        ["kiosk.addHint"] = new[] { "填网址，例如 oa.tlstudio.cn 或 http://10.0.0.5:8080/", "Enter a site, e.g. oa.tlstudio.cn or http://10.0.0.5:8080/" },
        ["kiosk.add"] = new[] { "添加", "Add" },
        ["kiosk.remove"] = new[] { "删除选中", "Remove" },
        ["kiosk.passLabel"] = new[] { "管理员密码", "Admin password" },
        ["kiosk.newPassLabel"] = new[] { "新管理员密码（留空则不改）", "New admin password (leave blank to keep)" },
        ["kiosk.pass2Label"] = new[] { "再输一次", "Repeat" },
        ["kiosk.sameHost"] = new[] { "同时放行这些域名的子域名（填 tlstudio.cn 就连带 oa.tlstudio.cn）",
                                     "Also allow subdomains of these sites (tlstudio.cn also covers oa.tlstudio.cn)" },
        ["kiosk.hint"] = new[] { "名单之外一律拦下，包括搜索和外链。密码不会存明文，只存摘要，忘了只能删掉配置重设。\n这个模式管的是员工在浏览器里能逛到哪，不是防拿到本机管理员权限的人。",
                                 "Anything not on the list is blocked, including search and outbound links. The password is stored as a hash, never in plain text — lose it and you must reset the config.\nThis controls where staff can browse; it does not stop someone with admin rights on this PC." },
        ["kiosk.enable"] = new[] { "启用企业模式", "Enable" },
        ["kiosk.unlock"] = new[] { "解锁设置", "Unlock" },
        ["kiosk.save"] = new[] { "保存", "Save" },
        ["kiosk.disable"] = new[] { "关闭企业模式", "Turn off" },
        ["kiosk.cancel"] = new[] { "取消", "Cancel" },
        ["kiosk.needUrl"] = new[] { "请至少添加一个允许访问的网址。", "Add at least one allowed site." },
        ["kiosk.badUrl"] = new[] { "这个网址认不出来，换一个（例如 oa.tlstudio.cn 或 http://10.0.0.5:8080/）。",
                                   "That doesn't look like a site — try something like oa.tlstudio.cn or http://10.0.0.5:8080/." },
        ["kiosk.dupUrl"] = new[] { "这个网址已经在名单里了。", "That site is already on the list." },
        ["kiosk.needPass"] = new[] { "请设置管理员密码（不能为空）。改名单和退出企业模式都要用它。",
                                     "Set an admin password (it can't be empty). You'll need it to change the list or leave this mode." },
        ["kiosk.passMismatch"] = new[] { "两次输入的密码不一致。", "The two passwords don't match." },
        ["kiosk.badPass"] = new[] { "密码不对。", "Wrong password." },
        ["kiosk.enabled"] = new[] { "企业模式已开启，员工只能访问名单里的 {0} 个网站。", "Enterprise mode is on — staff can only reach the {0} sites on the list." },
        ["kiosk.disabled"] = new[] { "企业模式已关闭。", "Enterprise mode is off." },
        ["kiosk.saved"] = new[] { "已保存。", "Saved." },
        ["kiosk.passChanged"] = new[] { "管理员密码已修改。", "Admin password updated." },
        ["kiosk.blocked"] = new[] { "企业模式：这个网站不在允许名单里。", "Enterprise mode: this site isn't on the allowed list." },

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

        // ── 工具箱：类别 ──
        ["toolbox.menu"] = new[] { "工具箱", "Toolbox" },
        ["toolbox.window"] = new[] { "{0} · 工具箱", "{0} · Toolbox" },
        ["toolbox.title"] = new[] { "工具箱", "Toolbox" },
        ["tool.title"] = new[] { "工具箱 · {0}", "Toolbox · {0}" },
        ["toolbox.cat.ops"] = new[] { "运维 / IT", "Ops / IT" },
        ["toolbox.cat.web"] = new[] { "网页 / 浏览", "Web / Browsing" },
        ["toolbox.cat.text"] = new[] { "文本 / 日常", "Text / Everyday" },
        ["toolbox.cat.safe"] = new[] { "安全 / 隐私", "Security / Privacy" },
        ["toolbox.count"] = new[] { "共 {0} 项功能", "{0} tools" },
        ["toolbox.doneTitle"] = new[] { "{0} · 完成", "{0} · Done" },
        ["toolbox.confirmTitle"] = new[] { "{0} · 请确认", "{0} · Confirm" },
        ["toolbox.inputTitle"] = new[] { "{0} · 输入", "{0} · Input" },
        ["toolbox.resultTitle"] = new[] { "{0} · 结果", "{0} · Result" },
        ["toolbox.run"] = new[] { "执行", "Run" },
        ["toolbox.copy"] = new[] { "复制结果", "Copy result" },
        ["toolbox.copied"] = new[] { "已复制到剪贴板。", "Copied to clipboard." },
        ["toolbox.clear"] = new[] { "清空", "Clear" },
        ["toolbox.close"] = new[] { "关闭", "Close" },
        ["toolbox.empty"] = new[] { "请先填内容。", "Enter something first." },

        // ── 工具箱：100 项功能 ──
        ["tool.ops.subnet"] = new[] { "子网掩码计算", "Subnet calculator" },
        ["tool.ops.ip2int"] = new[] { "IP 转整数", "IP to integer" },
        ["tool.ops.int2ip"] = new[] { "整数转 IP", "Integer to IP" },
        ["tool.ops.port"] = new[] { "端口对照查询", "Port lookup" },
        ["tool.ops.ipclass"] = new[] { "IP 地址分类", "IP address class" },
        ["tool.ops.cidr"] = new[] { "网段包含检查", "CIDR overlap check" },
        ["tool.ops.mac"] = new[] { "MAC 地址格式化", "Format MAC address" },
        ["tool.ops.ts2date"] = new[] { "时间戳转日期", "Timestamp to date" },
        ["tool.ops.date2ts"] = new[] { "日期转时间戳", "Date to timestamp" },
        ["tool.ops.utc"] = new[] { "UTC / 本地时间", "UTC / local time" },
        ["tool.ops.hex"] = new[] { "十六进制 ↔ 文本", "Hex ↔ text" },
        ["tool.ops.b64enc"] = new[] { "Base64 编码", "Base64 encode" },
        ["tool.ops.b64dec"] = new[] { "Base64 解码", "Base64 decode" },
        ["tool.ops.urlenc"] = new[] { "URL 编码", "URL encode" },
        ["tool.ops.urldec"] = new[] { "URL 解码", "URL decode" },
        ["tool.ops.md5"] = new[] { "MD5 计算", "MD5 hash" },
        ["tool.ops.sha1"] = new[] { "SHA-1 计算", "SHA-1 hash" },
        ["tool.ops.sha256"] = new[] { "SHA-256 计算", "SHA-256 hash" },
        ["tool.ops.regex"] = new[] { "正则测试", "Regex tester" },
        ["tool.ops.jsonfmt"] = new[] { "JSON 格式化", "Format JSON" },
        ["tool.ops.jsonmin"] = new[] { "JSON 压缩", "Minify JSON" },
        ["tool.ops.machine"] = new[] { "本机信息", "System info" },
        ["tool.ops.myip"] = new[] { "本机 IP 地址", "Local IP addresses" },
        ["tool.ops.disk"] = new[] { "磁盘剩余空间", "Disk space" },
        ["tool.ops.newline"] = new[] { "换行符统一", "Normalize line endings" },
        ["tool.web.shot"] = new[] { "网页截图", "Screenshot" },
        ["tool.web.shotfull"] = new[] { "整页截图", "Full-page screenshot" },
        ["tool.web.print"] = new[] { "打印当前页", "Print page" },
        ["tool.web.pdf"] = new[] { "导出为 PDF", "Export as PDF" },
        ["tool.web.info"] = new[] { "页面信息", "Page info" },
        ["tool.web.links"] = new[] { "提取所有链接", "Extract links" },
        ["tool.web.images"] = new[] { "提取所有图片", "Extract images" },
        ["tool.web.copytitle"] = new[] { "复制页面标题", "Copy page title" },
        ["tool.web.copyurl"] = new[] { "复制页面地址", "Copy page URL" },
        ["tool.web.copymd"] = new[] { "复制 Markdown 链接", "Copy as Markdown link" },
        ["tool.web.copytext"] = new[] { "复制正文纯文本", "Copy page text" },
        ["tool.web.find"] = new[] { "页内查找", "Find on page" },
        ["tool.web.source"] = new[] { "查看网页源代码", "View page source" },
        ["tool.web.headers"] = new[] { "查看响应头", "View response headers" },
        ["tool.web.reader"] = new[] { "阅读模式", "Reader mode" },
        ["tool.web.dark"] = new[] { "夜间模式", "Dark mode" },
        ["tool.web.nojs"] = new[] { "禁止网页脚本", "Disable scripts" },
        ["tool.web.noimg"] = new[] { "禁止网页图片", "Disable images" },
        ["tool.web.translate"] = new[] { "翻译当前页", "Translate page" },
        ["tool.web.zoom100"] = new[] { "缩放复位 100%", "Reset zoom to 100%" },
        ["tool.web.zoomin"] = new[] { "放大 10%", "Zoom in 10%" },
        ["tool.web.zoomout"] = new[] { "缩小 10%", "Zoom out 10%" },
        ["tool.web.seltext"] = new[] { "复制选中文字", "Copy selection" },
        ["tool.web.sysopen"] = new[] { "用系统浏览器打开", "Open in system browser" },
        ["tool.web.clearsite"] = new[] { "清除本页站点数据", "Clear this site's data" },
        ["tool.web.resettrial"] = new[] { "重置免费次数", "Reset free-trial limit" },
        ["tool.text.count"] = new[] { "字数统计", "Count characters" },
        ["tool.text.upper"] = new[] { "转大写", "UPPERCASE" },
        ["tool.text.lower"] = new[] { "转小写", "lowercase" },
        ["tool.text.title"] = new[] { "首字母大写", "Title Case" },
        ["tool.text.swap"] = new[] { "大小写互换", "Swap case" },
        ["tool.text.camel2snake"] = new[] { "驼峰转下划线", "camelCase → snake_case" },
        ["tool.text.snake2camel"] = new[] { "下划线转驼峰", "snake_case → camelCase" },
        ["tool.text.noblank"] = new[] { "去除空行", "Remove blank lines" },
        ["tool.text.trim"] = new[] { "去除行首尾空白", "Trim each line" },
        ["tool.text.nospace"] = new[] { "去除所有空格", "Remove all spaces" },
        ["tool.text.dedup"] = new[] { "行去重", "Remove duplicate lines" },
        ["tool.text.sort"] = new[] { "行排序", "Sort lines" },
        ["tool.text.reverse"] = new[] { "行倒序", "Reverse line order" },
        ["tool.text.alpha"] = new[] { "按字母排序", "Sort lines A-Z" },
        ["tool.text.bylen"] = new[] { "按长度排序", "Sort lines by length" },
        ["tool.text.shuffle"] = new[] { "行洗牌", "Shuffle lines" },
        ["tool.text.addnum"] = new[] { "加行号", "Add line numbers" },
        ["tool.text.delnum"] = new[] { "去行号", "Remove line numbers" },
        ["tool.text.prefix"] = new[] { "加前缀", "Add prefix" },
        ["tool.text.suffix"] = new[] { "加后缀", "Add suffix" },
        ["tool.text.reversechars"] = new[] { "文本反转", "Reverse text" },
        ["tool.text.wide2narrow"] = new[] { "全角转半角", "Full-width → half-width" },
        ["tool.text.narrow2wide"] = new[] { "半角转全角", "Half-width → full-width" },
        ["tool.text.t2s"] = new[] { "繁简转换", "Traditional ↔ Simplified" },
        ["tool.text.replace"] = new[] { "查找替换", "Find and replace" },
        ["tool.safe.genpass"] = new[] { "密码生成器", "Password generator" },
        ["tool.safe.pass16"] = new[] { "生成 16 位强密码", "Generate 16-char password" },
        ["tool.safe.pass24"] = new[] { "生成 24 位强密码", "Generate 24-char password" },
        ["tool.safe.strength"] = new[] { "密码强度检测", "Password strength" },
        ["tool.safe.entropy"] = new[] { "密码熵值估算", "Password entropy" },
        ["tool.safe.uuid"] = new[] { "生成 UUID v4", "Generate UUID v4" },
        ["tool.safe.uuid10"] = new[] { "批量生成 10 个 UUID", "Generate 10 UUIDs" },
        ["tool.safe.hexrand"] = new[] { "随机十六进制串", "Random hex string" },
        ["tool.safe.verifyhash"] = new[] { "哈希校验", "Verify hash" },
        ["tool.safe.filemd5"] = new[] { "文件 MD5", "File MD5" },
        ["tool.safe.filesha256"] = new[] { "文件 SHA-256", "File SHA-256" },
        ["tool.safe.pii"] = new[] { "敏感信息检测", "Detect sensitive data" },
        ["tool.safe.idcard"] = new[] { "身份证号校验", "Validate ID number" },
        ["tool.safe.luhn"] = new[] { "银行卡号校验", "Validate card number" },
        ["tool.safe.urldanger"] = new[] { "URL 安全检查", "URL safety check" },
        ["tool.safe.weakpass"] = new[] { "弱口令匹配", "Weak password check" },
        ["tool.safe.clearcookie"] = new[] { "清除 Cookie", "Clear cookies" },
        ["tool.safe.clearhistory"] = new[] { "清除浏览历史", "Clear history" },
        ["tool.safe.clearcache"] = new[] { "清除缓存", "Clear cache" },
        ["tool.safe.adblock"] = new[] { "广告拦截开关", "Toggle ad blocking" },
        ["tool.safe.adstats"] = new[] { "拦截统计", "Blocking stats" },
        ["tool.safe.adlog"] = new[] { "拦截日志", "Blocking log" },
        ["tool.safe.clearsites"] = new[] { "清除全部站点数据", "Clear all site data" },
        ["tool.safe.privacyreport"] = new[] { "隐私报告", "Privacy report" },
        ["tool.safe.cleanup"] = new[] { "一键隐私清理", "One-click privacy cleanup" },

        // ── 工具箱：追加的 12 项（v1.1.8）──
        ["tool.ops.radix"] = new[] { "进制转换", "Radix converter" },
        ["tool.ops.cron"] = new[] { "Cron 表达式解析", "Cron explainer" },
        ["tool.ops.urlparts"] = new[] { "URL 参数拆解", "URL parts" },
        ["tool.web.unlockcopy"] = new[] { "解除网页复制限制", "Unlock copy/paste" },
        ["tool.web.outline"] = new[] { "提取页面标题大纲", "Page outline" },
        ["tool.web.copyhtml"] = new[] { "导出网页 HTML 源码", "Export page HTML" },
        ["tool.text.rmb"] = new[] { "数字转人民币大写", "Number to RMB words" },
        ["tool.text.numfmt"] = new[] { "数字千分位", "Thousands separator" },
        ["tool.text.sortnum"] = new[] { "按数字大小排序", "Numeric sort" },
        ["tool.safe.rand"] = new[] { "随机数生成器", "Random numbers" },
        ["tool.safe.validate"] = new[] { "格式校验", "Format checker" },
        ["tool.safe.mask"] = new[] { "敏感信息打码", "Mask sensitive data" },

        // ── 工具箱：功能体内部文案 ──
        ["toolbox.badIp"] = new[] { "这不是一个有效的 IPv4 地址。", "That's not a valid IPv4 address." },
        ["toolbox.badNumber"] = new[] { "请填一个数字。", "Enter a number." },
        ["toolbox.badPort"] = new[] { "端口号要在 0 ~ 65535 之间。", "A port must be between 0 and 65535." },
        ["toolbox.badPrefix"] = new[] { "子网前缀要在 0 ~ 32 之间（写成 192.168.1.1/24 这样）。", "The prefix must be 0-32 (write it as 192.168.1.1/24)." },
        ["toolbox.badDate"] = new[] { "认不出这个日期。可以写成 2026-10-11 或 2026-10-11 16:53:00。", "Can't parse that date. Try 2026-10-11 or 2026-10-11 16:53:00." },
        ["toolbox.badBase64"] = new[] { "这不是有效的 Base64 内容。", "That isn't valid Base64." },
        ["toolbox.error"] = new[] { "出错了：", "Error: " },

        // ── 工具箱：追加功能的内部文案（v1.1.8）──
        ["cron.head"] = new[] { "这个 cron 想说的是：", "This cron means:" },
        ["cron.bad"] = new[] { "cron 要 5 段：分钟 小时 日期 月份 星期，例如 */5 9-18 * * 1-5。", "cron needs 5 fields: minute hour day month weekday, e.g. */5 9-18 * * 1-5." },
        ["urlparts.bad"] = new[] { "这不是一个完整的网址，要带上 http:// 或 https://。", "That isn't a full URL — include http:// or https://." },
        ["web.unlockCopyDone"] = new[] { "已解除本页的复制 / 右键限制。", "Copy and right-click restrictions lifted on this page." },
        ["web.outlineNone"] = new[] { "这一页没有用 h1~h6 标题。", "This page has no h1~h6 headings." },
        ["web.copyHtmlName"] = new[] { "网页源码", "page-source" },
        ["web.copyHtmlSaved"] = new[] { "源码已复制到剪贴板，并存到：{0}", "Source copied to clipboard and saved to: {0}" },
        ["web.copyHtmlCopied"] = new[] { "源码已复制到剪贴板（{0} 字节）。", "Source copied to clipboard ({0} bytes)." },
        ["rmb.negative"] = new[] { "先只支持非负数。", "Only non-negative numbers for now." },
        ["rmb.tooBig"] = new[] { "数字太大了，超出能读的范围。", "That number is too large to read out." },
        ["rand.bad"] = new[] { "写成这样：100，或 1-100，或 1-100 x5（x 后面是个数）。", "Try: 100, or 1-100, or 1-100 x5 (x = how many)." },
        ["validate.head"] = new[] { "这段文本看起来像：", "This text looks like:" },
        ["validate.none"] = new[] { "认不出是常见格式（邮箱 / 手机号 / 网址 / IP / 身份证 / 银行卡 / 日期 / UUID）。", "Not a common format (email / mobile / URL / IP / ID card / bank card / date / UUID)." },
        ["validate.email"] = new[] { "邮箱地址", "Email address" },
        ["validate.mobile"] = new[] { "中国大陆手机号", "Mainland China mobile number" },
        ["validate.phone"] = new[] { "电话号码", "Phone number" },
        ["validate.url"] = new[] { "网址", "URL" },
        ["validate.ipv4"] = new[] { "IPv4 地址", "IPv4 address" },
        ["validate.ipv6"] = new[] { "IPv6 地址", "IPv6 address" },
        ["validate.idcard"] = new[] { "18 位身份证号", "18-digit ID card number" },
        ["validate.date"] = new[] { "日期（YYYY-MM-DD）", "Date (YYYY-MM-DD)" },
        ["validate.uuid"] = new[] { "UUID", "UUID" },
        ["validate.bankcard"] = new[] { "银行卡号", "Bank card number" },
        ["mask.head"] = new[] { "已打码 {0} 处：", "Masked {0} item(s):" },
        ["mask.none"] = new[] { "没发现手机号 / 邮箱 / 身份证 / 银行卡。", "No phone / email / ID card / bank card found." },
        ["subnet.input"] = new[] { "输入", "Input" },
        ["subnet.mask"] = new[] { "子网掩码", "Netmask" },
        ["subnet.network"] = new[] { "网络地址", "Network" },
        ["subnet.broadcast"] = new[] { "广播地址", "Broadcast" },
        ["subnet.wildcard"] = new[] { "反掩码", "Wildcard" },
        ["subnet.hosts"] = new[] { "可用主机数", "Usable hosts" },
        ["subnet.usable"] = new[] { "可用范围", "Usable range" },
        ["subnet.noUsable"] = new[] { "（/31、/32 没有常规可用地址）", "(no conventional host range for /31 or /32)" },
        ["port.found"] = new[] { "端口 {0}：{1}", "Port {0}: {1}" },
        ["port.system"] = new[] { "端口 {0}：系统保留端口（0-1023），一般由系统服务占用。", "Port {0}: system port (0-1023), usually taken by a system service." },
        ["port.user"] = new[] { "端口 {0}：用户注册端口（1024-49151），常见应用层服务会用这段。", "Port {0}: registered port (1024-49151) — common for application services." },
        ["port.dynamic"] = new[] { "端口 {0}：动态 / 私有端口（49152-65535），一般由客户端临时占用。", "Port {0}: dynamic/private port (49152-65535), usually taken temporarily by clients." },
        ["ipclass.input"] = new[] { "地址", "Address" },
        ["ipclass.class"] = new[] { "类别", "Class" },
        ["ipclass.scope"] = new[] { "范围", "Scope" },
        ["ipclass.private"] = new[] { "内网地址", "private" },
        ["ipclass.loopback"] = new[] { "回环地址", "loopback" },
        ["ipclass.linkLocal"] = new[] { "链路本地（APIPA）", "link-local (APIPA)" },
        ["ipclass.multicast"] = new[] { "组播", "multicast" },
        ["ipclass.public"] = new[] { "公网地址", "public" },
        ["cidr.needTwo"] = new[] { "至少填两行网段，一行一个，例如：\n192.168.1.0/24\n192.168.1.128/25", "Enter at least two networks, one per line, e.g.:\n192.168.1.0/24\n192.168.1.128/25" },
        ["cidr.same"] = new[] { "{0} 与 {1} 是同一个网段。", "{0} and {1} are the same network." },
        ["cidr.aInB"] = new[] { "{0} 包含在 {1} 里。", "{0} is contained in {1}." },
        ["cidr.bInA"] = new[] { "{1} 包含在 {0} 里。", "{1} is contained in {0}." },
        ["cidr.disjoint"] = new[] { "这些网段之间没有包含或重叠关系。", "These networks don't overlap or contain each other." },
        ["mac.bad"] = new[] { "MAC 地址要能凑出 12 位十六进制字符（例如 E0:62:90:96:19:4D）。", "A MAC address needs 12 hex characters (e.g. E0:62:90:96:19:4D)." },
        ["mac.colon"] = new[] { "冒号分隔", "Colon" },
        ["mac.hyphen"] = new[] { "横线分隔", "Hyphen" },
        ["mac.dot"] = new[] { "点分（Cisco）", "Dotted (Cisco)" },
        ["mac.plain"] = new[] { "无分隔", "Plain" },
        ["ts.unit"] = new[] { "识别为", "Detected as" },
        ["ts.second"] = new[] { "秒", "seconds" },
        ["ts.milli"] = new[] { "毫秒", "milliseconds" },
        ["ts.local"] = new[] { "本地时间", "Local time" },
        ["ts.utc"] = new[] { "UTC 时间", "UTC" },
        ["ts.iso"] = new[] { "ISO 8601", "ISO 8601" },
        ["utc.offset"] = new[] { "本地偏移", "Local offset" },
        ["utc.zone"] = new[] { "时区", "Time zone" },
        ["hex.decoded"] = new[] { "按十六进制解码：", "Decoded from hex:" },
        ["hex.encoded"] = new[] { "按十六进制编码：", "Encoded as hex:" },
        ["json.bad"] = new[] { "这段 JSON 有问题：", "This JSON is invalid:" },
        ["regex.needTwo"] = new[] { "第一行填正则，第二行起填要测试的文本。", "Put the pattern on line 1 and the test text from line 2 on." },
        ["regex.matches"] = new[] { "匹配到 {0} 处：", "{0} match(es):" },
        ["regex.bad"] = new[] { "正则写错了：", "Bad pattern:" },
        ["machine.name"] = new[] { "计算机名", "Host name" },
        ["machine.user"] = new[] { "当前用户", "User" },
        ["machine.os"] = new[] { "系统版本", "OS" },
        ["machine.arch"] = new[] { "架构", "Architecture" },
        ["machine.cores"] = new[] { "CPU 核心数", "CPU cores" },
        ["machine.dotnet"] = new[] { "运行时版本", ".NET version" },
        ["machine.runtime"] = new[] { "运行时描述", "Runtime" },
        ["machine.uptime"] = new[] { "已运行", "Uptime" },
        ["machine.boot"] = new[] { "本次开机时间", "Booted at" },
        ["myip.none"] = new[] { "没有找到已连接的网络接口。", "No connected network interface found." },
        ["disk.total"] = new[] { "总计", "total" },
        ["disk.free"] = new[] { "可用", "free" },
        ["disk.used"] = new[] { "已用", "used" },
        ["disk.none"] = new[] { "没有可读取的磁盘。", "No readable disk found." },
        ["newline.count"] = new[] { "CRLF（Windows）{0} 处　单独 CR {1} 处　单独 LF {2} 处", "CRLF (Windows): {0}　lone CR: {1}　lone LF: {2}" },
        ["newline.lf"] = new[] { "统一成 LF（Linux / macOS）", "Normalized to LF (Linux / macOS)" },
        ["newline.crlf"] = new[] { "统一成 CRLF（Windows）", "Normalized to CRLF (Windows)" },
        ["web.noPage"] = new[] { "当前没有打开的网页。", "No page is open right now." },
        ["web.badUrl"] = new[] { "当前地址不是普通的 http/https 网页。", "The current address isn't a regular http/https page." },
        ["web.shotSave"] = new[] { "保存网页截图", "Save screenshot" },
        ["web.shotFullSave"] = new[] { "保存整页截图", "Save full-page screenshot" },
        ["web.shotSaved"] = new[] { "截图已保存到：\n{0}", "Screenshot saved to:\n{0}" },
        ["web.shotFailed"] = new[] { "截图失败，可能是页面还没加载完，稍等一下再试。", "Capture failed — the page may still be loading. Try again in a moment." },
        ["web.pdfHint"] = new[] { "打印对话框里选「另存为 PDF」就能存成 PDF（建议文件名：{0}）。", "In the print dialog choose \"Save as PDF\" (suggested name: {0})." },
        ["pageinfo.title"] = new[] { "标题", "Title" },
        ["pageinfo.url"] = new[] { "地址", "URL" },
        ["pageinfo.host"] = new[] { "主机", "Host" },
        ["pageinfo.secure"] = new[] { "HTTPS 加密", "HTTPS" },
        ["pageinfo.yes"] = new[] { "是", "yes" },
        ["pageinfo.no"] = new[] { "否", "no" },
        ["pageinfo.charset"] = new[] { "编码", "Charset" },
        ["pageinfo.lang"] = new[] { "语言", "Language" },
        ["pageinfo.counts"] = new[] { "元素统计", "Elements" },
        ["web.linksCount"] = new[] { "共找到 {0} 个链接（已复制到剪贴板）：", "Found {0} link(s) — copied to clipboard:" },
        ["web.linksNone"] = new[] { "这个页面上没有链接。", "No links on this page." },
        ["web.imagesCount"] = new[] { "共找到 {0} 张图片（已复制到剪贴板）：", "Found {0} image(s) — copied to clipboard:" },
        ["web.imagesNone"] = new[] { "这个页面上没有图片。", "No images on this page." },
        ["headers.status"] = new[] { "状态", "Status" },
        ["headers.failed"] = new[] { "取响应头失败：", "Failed to fetch headers:" },
        ["web.readerOn"] = new[] { "已进入阅读模式（去掉了页面里的干扰元素）。", "Reader mode on — distracting elements removed." },
        ["web.darkOn"] = new[] { "已切到夜间模式。", "Dark mode on." },
        ["web.styleOff"] = new[] { "已恢复原来的样式。", "Restored the original styling." },
        ["web.nojsDone"] = new[] { "已禁止当前页运行脚本并重新加载。想恢复的话，再点一次这个功能。", "Scripts disabled and the page reloaded. Click this again to restore." },
        ["web.noimgDone"] = new[] { "已禁止当前页加载图片并重新加载。", "Images disabled and the page reloaded." },
        ["web.noimgBack"] = new[] { "已恢复加载图片并重新加载。", "Images enabled again and the page reloaded." },
        ["web.nothingSelected"] = new[] { "页面上没有选中文字。先选中一段再点这个功能。", "Nothing is selected. Select some text first." },
        ["web.clearSiteAsk"] = new[] { "清除 {0} 的站点数据（Cookie / localStorage / IndexedDB）？\n\n这个站的登录状态会掉。", "Clear site data for {0} (cookies / localStorage / IndexedDB)?\n\nYou'll be signed out of this site." },
        ["web.clearSiteDone"] = new[] { "已清除 {0} 的站点数据。", "Cleared site data for {0}." },
        ["web.resetTrialAsk"] = new[] { "重置 {0} 的免费次数？\n\n会把这个站存在你电脑上的痕迹清一遍（Cookie、本地存储、IndexedDB、缓存、Service Worker），然后刷新页面，免费次数就重新算了。\n\n· 只影响 {0}，别的网站不受影响\n· 这个站的登录状态会掉，要重新登\n· 清不掉「按 IP / 按账号」记的次数，那种只能换号或等恢复", "Reset the free-trial limit on {0}?\n\nThis wipes everything this site stored on your machine (cookies, local storage, IndexedDB, cache, service workers), then reloads the page so its free-usage counter starts over.\n\n· Affects {0} only — other sites are untouched\n· You'll be signed out of this site and have to log in again\n· Limits tracked by IP or account can't be cleared this way" },
        ["web.resetTrialDone"] = new[] { "已重置 {0} 的免费次数，页面刷新后就能接着用了。\n\n清理明细：{1}", "Free-trial limit on {0} has been reset. Reload the page and you're good to go.\n\nDetails: {1}" },
        ["web.resetTrialParts"] = new[] { "Cookie {0} 个 · 本地存储 {1} 项 · IndexedDB {2} 个 · 缓存 {3} 个 · Service Worker {4} 个", "{0} cookies · {1} local storage entries · {2} IndexedDB databases · {3} caches · {4} service workers" },
        ["count.chars"] = new[] { "总字符数", "Characters" },
        ["count.noSpace"] = new[] { "不含空格", "Excluding spaces" },
        ["count.cjk"] = new[] { "中日韩汉字", "CJK characters" },
        ["count.words"] = new[] { "英文单词", "Words" },
        ["count.lines"] = new[] { "行数", "Lines" },
        ["count.paras"] = new[] { "段落数", "Paragraphs" },
        ["affix.needTwo"] = new[] { "第一行填要加的内容，第二行起填正文。", "Put the affix on line 1 and the text from line 2 on." },
        ["replace.needThree"] = new[] { "第一行填「查找」，第二行填「替换」，第三行起填正文。", "Line 1 = find, line 2 = replace, line 3+ = the text." },
        ["replace.needFind"] = new[] { "「查找」不能为空。", "The \"find\" value can't be empty." },
        ["replace.count"] = new[] { "替换了 {0} 处：", "Replaced {0} occurrence(s):" },
        ["strength.empty"] = new[] { "先填一个密码。", "Enter a password first." },
        ["strength.len"] = new[] { "长度", "Length" },
        ["strength.classes"] = new[] { "字符种类", "Character classes" },
        ["strength.score"] = new[] { "评分", "Score" },
        ["strength.level"] = new[] { "等级", "Rating" },
        ["strength.weak"] = new[] { "弱", "Weak" },
        ["strength.fair"] = new[] { "一般", "Fair" },
        ["strength.good"] = new[] { "较强", "Good" },
        ["strength.strong"] = new[] { "很强", "Strong" },
        ["strength.common"] = new[] { "⚠ 命中常见弱口令表，建议换一个。", "⚠ Found in the common-password list — pick another one." },
        ["strength.tipLong"] = new[] { "建议至少 12 位。", "Aim for at least 12 characters." },
        ["strength.tipClass"] = new[] { "建议同时用上大小写、数字、符号。", "Mix upper/lower case, digits and symbols." },
        ["entropy.pool"] = new[] { "字符集大小", "Character pool" },
        ["entropy.bits"] = new[] { "信息熵", "Entropy" },
        ["entropy.crack"] = new[] { "暴力破解耗时（按每秒 1000 亿次估算）", "Brute-force time (at 100 billion guesses/sec)" },
        ["entropy.instant"] = new[] { "几乎瞬间", "almost instantly" },
        ["entropy.sec"] = new[] { "秒", "sec" },
        ["entropy.min"] = new[] { "分钟", "min" },
        ["entropy.hour"] = new[] { "小时", "hour" },
        ["entropy.day"] = new[] { "天", "days" },
        ["entropy.year"] = new[] { "年", "years" },
        ["verifyhash.needTwo"] = new[] { "第一行填文本，第二行填要核对的摘要。", "Line 1 = the text, line 2 = the digest to verify." },
        ["verifyhash.algo"] = new[] { "按摘要长度判断算法", "Algorithm inferred from length" },
        ["verifyhash.unknown"] = new[] { "未知长度", "unknown length" },
        ["verifyhash.lenBad"] = new[] { "摘要长度不是 32 / 40 / 64 位，认不出是什么算法。", "The digest isn't 32/40/64 chars, so the algorithm is unknown." },
        ["verifyhash.match"] = new[] { "✔ 一致，内容和摘要是对得上的。", "✔ Match — the content and digest agree." },
        ["verifyhash.mismatch"] = new[] { "✘ 不一致，内容被改过或者摘要填错了。", "✘ Mismatch — the content changed, or the digest is wrong." },
        ["filehash.pick"] = new[] { "选择要计算哈希的文件", "Pick a file to hash" },
        ["filehash.file"] = new[] { "文件", "File" },
        ["filehash.size"] = new[] { "大小", "Size" },
        ["pii.total"] = new[] { "扫描结果：发现 {0} 处可疑信息。", "Scan result: {0} potential item(s) found." },
        ["pii.none"] = new[] { "没有发现身份证、手机号、邮箱、银行卡这类信息。", "No ID numbers, phone numbers, emails or card numbers found." },
        ["pii.phone"] = new[] { "手机号", "Phone" },
        ["pii.email"] = new[] { "邮箱", "Email" },
        ["pii.idcard"] = new[] { "身份证号", "ID number" },
        ["pii.bank"] = new[] { "疑似银行卡号", "Possible card number" },
        ["pii.ssn"] = new[] { "疑似社保号", "Possible SSN" },
        ["idcard.format"] = new[] { "身份证号应是 18 位（最后一位可以是 X）。", "An ID number should be 18 chars (the last may be X)." },
        ["idcard.got"] = new[] { "第 18 位", "Check digit" },
        ["idcard.expect"] = new[] { "应为", "Expected" },
        ["idcard.ok"] = new[] { "✔ 校验位正确，这个号码格式上成立。", "✔ Check digit is correct — the number is well-formed." },
        ["idcard.bad"] = new[] { "✘ 校验位不对，这个号码是编的或者抄错了。", "✘ Check digit is wrong — the number is made up or mistyped." },
        ["idcard.birth"] = new[] { "出生日期", "Birth date" },
        ["idcard.age"] = new[] { "年龄约", "Age (approx.)" },
        ["idcard.gender"] = new[] { "性别", "Gender" },
        ["idcard.male"] = new[] { "男", "male" },
        ["idcard.female"] = new[] { "女", "female" },
        ["luhn.len"] = new[] { "银行卡号一般是 12 ~ 19 位数字。", "A card number is usually 12-19 digits." },
        ["luhn.digits"] = new[] { "数字位数", "Digits" },
        ["luhn.sum"] = new[] { "校验和", "Checksum" },
        ["luhn.ok"] = new[] { "✔ 通过 Luhn 校验，卡号格式上成立。", "✔ Passes the Luhn check — the number is well-formed." },
        ["luhn.bad"] = new[] { "✘ 没通过 Luhn 校验，卡号抄错了或者本来是编的。", "✘ Fails the Luhn check — mistyped or made up." },
        ["urldanger.url"] = new[] { "地址", "URL" },
        ["urldanger.host"] = new[] { "主机", "Host" },
        ["urldanger.clean"] = new[] { "✔ 没有发现明显的可疑特征。", "✔ No obvious red flags." },
        ["urldanger.ipHost"] = new[] { "直接用 IP 当网址，正常网站很少这样。", "Uses a bare IP as the host — legitimate sites rarely do." },
        ["urldanger.long"] = new[] { "地址特别长（{0} 个字符），可能是在藏真实域名。", "Unusually long ({0} chars) — may be hiding the real domain." },
        ["urldanger.punycode"] = new[] { "域名含 xn-- 编码，可能是仿冒同形字的假站。", "Contains punycode (xn--) — possibly a homograph look-alike." },
        ["urldanger.httpIp"] = new[] { "http + IP 直连，没有加密也没用域名，风险很高。", "http + bare IP: no encryption, no domain — high risk." },
        ["urldanger.http"] = new[] { "不是 HTTPS，传输内容可能被看到。", "Not HTTPS — traffic may be readable." },
        ["urldanger.tld"] = new[] { "用了 {0} 这类高风险的便宜顶级域。", "Uses the risky low-cost TLD {0}." },
        ["urldanger.userinfo"] = new[] { "地址里有 @ 形式的内嵌用户名，是在骗人看错域名。", "Has embedded credentials (user@) — a trick to mislead you about the domain." },
        ["urldanger.atSign"] = new[] { "带有 @ 符号，注意它前面那段不是真正的域名。", "Contains @ — the part before it is not the real domain." },
        ["urldanger.lookalike"] = new[] { "{0} 看着像自家域名，其实不是。", "{0} looks like one of our domains but isn't." },
        ["weakpass.len"] = new[] { "长度", "Length" },
        ["weakpass.noHit"] = new[] { "✔ 没命中内置弱口令表和常见规律。", "✔ No hit in the built-in weak list or common patterns." },
        ["weakpass.hits"] = new[] { "命中 {0} 条风险：", "{0} risk(s) found:" },
        ["weakpass.allDigits"] = new[] { "全是数字，太容易被穷举。", "All digits — trivially brute-forced." },
        ["weakpass.allLetters"] = new[] { "全是字母，字符集太窄。", "All letters — too narrow a character set." },
        ["weakpass.repeat"] = new[] { "有连续重复的字符（如 aaa）。", "Contains repeated runs of characters (e.g. aaa)." },
        ["weakpass.sequence"] = new[] { "含键盘或数字顺序（如 123、qwe）。", "Contains a sequence (e.g. 123, qwe)." },
        ["safe.adStatsBody"] = new[] { "本次运行共拦截 {0} 条广告或跟踪请求。\n当前生效规则 {1} 条。", "Blocked {0} ad/tracker request(s) this session.\n{1} rules are active." },
        ["safe.adLogEmpty"] = new[] { "还没有拦截记录。", "No blocked entries yet." },
        ["safe.adLogHead"] = new[] { "最近 {0} 条拦截记录：", "Last {0} blocked entries:" },
        ["safe.adLogPath"] = new[] { "完整日志文件", "Full log file" },
        ["safe.clearCookieAsk"] = new[] { "清除当前站点的 Cookie？\n\n这个站的登录状态会掉。", "Clear cookies for the current site?\n\nYou'll be signed out of it." },
        ["safe.clearCookieDone"] = new[] { "已清除当前站点的 Cookie。", "Cleared cookies for the current site." },
        ["safe.clearHistoryAsk"] = new[] { "清除全部浏览历史记录？\n\n只删记录，不影响已保存的密码和书签。", "Clear the entire browsing history?\n\nOnly the list is cleared — saved passwords and bookmarks stay." },
        ["safe.clearHistoryDone"] = new[] { "已清除 {0} 条浏览历史。", "Cleared {0} history entries." },
        ["safe.clearCacheAsk"] = new[] { "清除浏览器缓存？\n\n页面下次打开会慢一点，不会影响登录状态。", "Clear the browser cache?\n\nPages may load a bit slower next time; sign-ins stay." },
        ["safe.clearCacheHint"] = new[] { "缓存由内核自己管理，建议在「设置」里清一次，或者直接重启浏览器。", "The cache is managed by the engine — clear it from Settings, or just restart the browser." },
        ["safe.clearSitesAsk"] = new[] { "清除所有网站的本地存储（localStorage / IndexedDB）？\n\n各站的登录状态和本地设置会掉。", "Clear local storage for all sites (localStorage / IndexedDB)?\n\nYou'll be signed out everywhere." },
        ["safe.clearSitesHint"] = new[] { "站点数据由内核自己管理，重启浏览器后生效。", "Site data is managed by the engine and clears after a restart." },
        ["safe.cleanupAsk"] = new[] { "一键清理会依次清除 Cookie、浏览历史、站点数据。\n\n确定继续吗？", "This clears cookies, history and site data in turn.\n\nContinue?" },
        ["safe.cleanupHead"] = new[] { "一键隐私清理结果：", "One-click cleanup result:" },
        ["adblock.onTurned"] = new[] { "已开启广告拦截。", "Ad blocking enabled." },
        ["adblock.offTurned"] = new[] { "已关闭广告拦截。", "Ad blocking disabled." },
        ["privacy.head"] = new[] { "隐私报告", "Privacy report" },
        ["privacy.history"] = new[] { "浏览历史", "History entries" },
        ["privacy.blocked"] = new[] { "本次拦截", "Blocked this session" },
        ["privacy.rules"] = new[] { "生效规则", "Active rules" },
        ["privacy.adblock"] = new[] { "广告拦截", "Ad blocking" },
        ["privacy.on"] = new[] { "已开启", "on" },
        ["privacy.off"] = new[] { "已关闭", "off" },
        ["privacy.dataDir"] = new[] { "数据目录", "Data folder" },
        ["privacy.cacheSize"] = new[] { "缓存占用", "Cache size" },
        ["privacy.current"] = new[] { "当前页面", "Current page" },
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

    /// <summary>
    /// 这个 key 是不是真的没定义。给自检用：T 缺键会原样回显 key，
    /// 光看返回值分不出「没定义」和「文案就是 key」。
    /// </summary>
    public static bool Missing(string key) => !Pack.TryGetValue(key, out _);

    /// <summary>翻译时目标语言代码。中文界面翻成英文，反过来翻成简体中文。</summary>
    public static string TranslateTo => IsEn ? "en" : "zh-Hans";
}
