using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TLBrowser;

/// <summary>主页守护面板：本次拦下了什么、系统层面有没有被动手脚。</summary>
internal sealed class GuardForm : Form
{
    private readonly Label _status = new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 9f),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };

    private readonly ListView _lvGuard = MakeList(
        (Lang.T("guard.col.time"), 76), (Lang.T("guard.col.status"), 62),
        (Lang.T("guard.col.kind"), 74), (Lang.T("guard.col.detail"), 468));

    private readonly ListView _lvSystem = MakeList(
        (Lang.T("guard.col.cat"), 72), (Lang.T("guard.col.item"), 150),
        (Lang.T("guard.col.detail"), 458));

    public GuardForm()
    {
        Text = Lang.T("guard.window", Brand.AppName);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 540);
        ShowInTaskbar = false;
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

        var header = BuildHeader();
        var lblGuard = MakeCaption(Lang.T("guard.blocked"));
        var lblSystem = MakeCaption(Lang.T("guard.system"));
        var bar = BuildButtonBar();

        _lvGuard.Height = 136;
        _lvSystem.Dock = DockStyle.Fill;

        // 停靠是按倒序处理的，Fill 的必须先加进来，否则会被 Top/Bottom 挤成 0 高
        Controls.Add(_lvSystem);
        Controls.Add(bar);
        Controls.Add(lblSystem);
        Controls.Add(_lvGuard);
        Controls.Add(lblGuard);
        Controls.Add(header);

        Reload();
    }

    // ────────────────────────────── 构建 ──────────────────────────────

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Color.White };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Brand.ChromeLine, 1f);
            e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };

        var glyph = new Label
        {
            AutoSize = false,
            Text = "\uEA18",
            Font = new Font("Segoe MDL2 Assets", 20f),
            ForeColor = Brand.Blue,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(20, 14, 40, 40)
        };

        var title = new Label
        {
            AutoSize = false,
            Text = Lang.T("guard.title"),
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Brand.TextMain,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(66, 13, 300, 26)
        };

        _status.Bounds = new Rectangle(67, 38, ClientSize.Width - 78, 20);

        panel.Controls.Add(glyph);
        panel.Controls.Add(title);
        panel.Controls.Add(_status);
        return panel;
    }

    private static Label MakeCaption(string text) => new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(18, 4, 0, 0),
        ForeColor = Brand.TextMain,
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
        BackColor = Color.White
    };

    private static ListView MakeList(params (string Text, int Width)[] columns)
    {
        var lv = new ListView
        {
            Dock = DockStyle.Top,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.75f)
        };
        foreach (var (text, width) in columns) lv.Columns.Add(text, width);
        return lv;
    }

    private Control BuildButtonBar()
    {
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.White };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Brand.ChromeLine, 1f);
            e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
        };

        var close = new PillButton(Lang.T("common.close"), Brand.Blue, true);
        close.Click += (_, _) => Close();

        var log = new PillButton(Lang.T("guard.openLog"), Brand.Blue, false);
        log.Click += (_, _) => OpenLog();

        var fix = new PillButton(Lang.T("guard.fix"), Brand.Purple, false);
        fix.Click += (_, _) => FixHijack();

        var rescan = new PillButton(Lang.T("guard.rescan"), Brand.Blue, false);
        rescan.Click += (_, _) => { HomeGuard.ScanSystem(); Reload(); };

        panel.Controls.Add(close);
        panel.Controls.Add(log);
        panel.Controls.Add(fix);
        panel.Controls.Add(rescan);
        panel.Resize += (_, _) =>
        {
            const int y = 13;
            var x = 18;
            rescan.SetBounds(x, y, 96, 32); x += 104;
            fix.SetBounds(x, y, 118, 32); x += 124;
            log.SetBounds(x, y, 118, 32);
            close.SetBounds(panel.ClientSize.Width - 110, y, 92, 32);
        };
        return panel;
    }

    // ────────────────────────────── 数据 ──────────────────────────────

    private static readonly Color Warn = Color.FromArgb(0xC0, 0x6A, 0x00);
    private static readonly Color Ok = Color.FromArgb(0x1B, 0x8A, 0x4B);

    private void Reload()
    {
        _lvGuard.BeginUpdate();
        _lvGuard.Items.Clear();
        foreach (var e in HomeGuard.Session) _lvGuard.Items.Add(GuardRow(e));
        if (_lvGuard.Items.Count == 0)
        {
            var idle = new ListViewItem(new[] { "—", Lang.T("guard.statusOk"), "—", Lang.T("guard.idle") });
            idle.ForeColor = Brand.TextDim;
            _lvGuard.Items.Add(idle);
        }
        _lvGuard.EndUpdate();

        _lvSystem.BeginUpdate();
        _lvSystem.Items.Clear();
        foreach (var e in HomeGuard.SystemChecks) _lvSystem.Items.Add(SystemRow(e));
        _lvSystem.EndUpdate();

        var attention = HomeGuard.AttentionCount;
        _status.Text = DescribeStatus(attention);
        _status.ForeColor = attention > 0 ? Warn : Brand.TextDim;
    }

    private static ListViewItem GuardRow(GuardEvent e)
    {
        var item = new ListViewItem(new[] { e.TimeText, e.StatusText, e.KindText, e.Detail })
        {
            ForeColor = e.Handled ? Brand.TextMain : Warn
        };
        // 状态列单独上色，扫一眼就知道哪条是真被处理过的
        item.SubItems[1].ForeColor = e.Handled ? Ok : Warn;
        item.ToolTipText = e.Item + "\r\n" + e.Detail;
        return item;
    }

    private static ListViewItem SystemRow(GuardEvent e)
    {
        var item = new ListViewItem(new[] { e.Category, e.Item, e.Detail })
        {
            ForeColor = e.Attention ? Warn : Brand.TextMain
        };
        item.ToolTipText = e.Item + "\n" + e.Detail +
                           (string.IsNullOrEmpty(e.Extra) ? "" : "\n\n" + e.Extra);
        return item;
    }

    /// <summary>
    /// 头部那行摘要。要短 —— 表头空间只有一行，写长了会被截断成「…」，
    /// 详细解释都塞进下面表格的悬停提示里。
    /// </summary>
    private static string DescribeStatus(int attention)
    {
        var handled = HomeGuard.HandledCount;
        var parts = new List<string>
        {
            handled > 0 ? Lang.T("guard.sumHandled", handled) : Lang.T("guard.sumNone"),
            attention > 0 ? Lang.T("guard.sumAttention", attention) : Lang.T("guard.sumClean")
        };
        return Lang.T("guard.sumPrefix") + "　·　" + string.Join("　·　", parts);
    }

    // ────────────────────────────── 动作 ──────────────────────────────

    private void OpenLog()
    {
        try
        {
            if (!File.Exists(Brand.GuardLogPath))
            {
                MessageBox.Show(this, Lang.T("guard.noLog"), Brand.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(Brand.GuardLogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Lang.T("guard.logFailed", ex.Message), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void FixHijack()
    {
        HomeGuard.ScanSystem();
        Reload();

        const string cp = @"HKCU\Software\Policies\Microsoft\Internet Explorer\Control Panel";

        var answer = MessageBox.Show(this,
            Lang.T("guard.fixAsk", cp),
            Lang.T("guard.fixTitle", Brand.AppName),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return;

        var result = HomeGuard.FixHomepageHijack(out var backupPath);
        HomeGuard.ScanSystem();
        Reload();

        MessageBox.Show(this,
            Lang.T("guard.fixDone", result, backupPath),
            Lang.T("guard.fixDoneTitle", Brand.AppName), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
