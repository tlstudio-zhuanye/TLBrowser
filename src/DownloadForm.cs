using System.Diagnostics;

namespace TLBrowser;

/// <summary>
/// 下载管理面板。上半是「正在下载」，下半是「下载记录」。
/// 进度每秒刷好几次（网页下载就是这么快），所以用 250ms 的定时器做节流，
/// 攒够再一次性重画，别让 UI 线程被进度条事件打爆。
/// </summary>
internal sealed class DownloadForm : Form
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

    private readonly ListView _lvActive = MakeList(
        (Lang.T("dl.col.file"), 300), (Lang.T("dl.col.size"), 92),
        (Lang.T("dl.col.progress"), 120), (Lang.T("dl.col.source"), 158));

    private readonly ListView _lvHistory = MakeList(
        (Lang.T("dl.col.file"), 300), (Lang.T("dl.col.status"), 72),
        (Lang.T("dl.col.size"), 84), (Lang.T("dl.col.time"), 130),
        (Lang.T("dl.col.source"), 84));

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private bool _dirty = true;

    public DownloadForm()
    {
        Text = Brand.AppName + " · 下载";
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(680, 480);
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
        var lblActive = MakeCaption("正在下载");
        var lblHistory = MakeCaption("下载记录");
        var bar = BuildButtonBar();

        _lvActive.Height = 150;
        _lvHistory.Dock = DockStyle.Fill;

        // Dock 按倒序处理，Fill 的先加进来，否则会被 Top/Bottom 挤成 0 高
        Controls.Add(_lvHistory);
        Controls.Add(bar);
        Controls.Add(lblHistory);
        Controls.Add(_lvActive);
        Controls.Add(lblActive);
        Controls.Add(header);

        DownloadStore.Changed += () => _dirty = true;
        _timer.Tick += (_, _) => { if (_dirty) { _dirty = false; Reload(); } };
        _timer.Start();

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
            Text = "\uE896",     // 下载箭头图标
            Font = new Font("Segoe MDL2 Assets", 20f),
            ForeColor = Brand.Blue,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(20, 14, 40, 40)
        };

        var title = new Label
        {
            AutoSize = false,
            Text = Lang.T("dl.title"),
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

        var openFile = new PillButton(Lang.T("dl.openFile"), Brand.Blue, true);
        openFile.Click += (_, _) => OpenSelectedFile();

        var openFolder = new PillButton(Lang.T("dl.openFolder"), Brand.Blue, false);
        openFolder.Click += (_, _) => OpenSelectedFolder();

        var cancel = new PillButton(Lang.T("dl.cancelSel"), Brand.Purple, false);
        cancel.Click += (_, _) => CancelSelected();

        var remove = new PillButton(Lang.T("dl.delete"), Brand.Purple, false);
        remove.Click += (_, _) => RemoveSelected();

        var clear = new PillButton(Lang.T("dl.clear"), Brand.Purple, false);
        clear.Click += (_, _) => ClearHistory();

        var close = new PillButton(Lang.T("dl.close"), Brand.Blue, false);
        close.Click += (_, _) => Close();

        panel.Controls.Add(openFile);
        panel.Controls.Add(openFolder);
        panel.Controls.Add(cancel);
        panel.Controls.Add(remove);
        panel.Controls.Add(clear);
        panel.Controls.Add(close);

        panel.Resize += (_, _) =>
        {
            const int y = 13, h = 32;
            var x = 18;
            foreach (var (b, w) in new[] {
                (openFile, 96), (openFolder, 132), (cancel, 120), (remove, 96), (clear, 96)
            })
            {
                b.SetBounds(x, y, w, h);
                x += w + 8;
            }
            close.SetBounds(panel.ClientSize.Width - 92, y, 74, h);
        };
        return panel;
    }

    // ────────────────────────────── 数据 ──────────────────────────────

    private void Reload()
    {
        ReloadActive();
        ReloadHistory();

        var active = DownloadStore.ActiveCount;
        var hist = DownloadStore.HistoryCount;
        _status.Text = active > 0
            ? $"正在下载 {active} 项　·　历史记录 {hist} 条"
            : $"没有正在进行的下载　·　历史记录 {hist} 条";
    }

    private void ReloadActive()
    {
        var list = DownloadStore.ActiveList;

        // 内容没变就不重画，减少闪烁；空列表且已画过占位行也算「没变」
        if (list.Count == 0)
        {
            if (_lvActive.Items.Count == 1 && _lvActive.Items[0].Tag is null) return;
        }
        else if (list.Count == _lvActive.Items.Count &&
                 !_lvActive.Items.Cast<ListViewItem>()
                     .Zip(list).Any(p => !ActiveRowEqual(p.First, p.Second)))
        {
            return;
        }

        _lvActive.BeginUpdate();
        _lvActive.Items.Clear();
        foreach (var d in list) _lvActive.Items.Add(ActiveRow(d));
        if (_lvActive.Items.Count == 0)
        {
            var idle = new ListViewItem(new[] { "当前没有正在进行的下载", "—", "—", "—" });
            idle.ForeColor = Brand.TextDim;
            _lvActive.Items.Add(idle);
        }
        _lvActive.EndUpdate();
    }

    private void ReloadHistory()
    {
        var list = DownloadStore.Recent(200);

        if (list.Count == 0)
        {
            if (_lvHistory.Items.Count == 1 && _lvHistory.Items[0].Tag is null) return;
        }
        else if (list.Count == _lvHistory.Items.Count &&
                 !_lvHistory.Items.Cast<ListViewItem>()
                     .Zip(list).Any(p => p.First.Tag != p.Second))
        {
            return;
        }

        _lvHistory.BeginUpdate();
        _lvHistory.Items.Clear();
        foreach (var d in list) _lvHistory.Items.Add(HistoryRow(d));
        if (_lvHistory.Items.Count == 0)
        {
            var idle = new ListViewItem(new[] { Lang.T("dl.idleHistory"), "—", "—", "—", "—" });
            idle.ForeColor = Brand.TextDim;
            _lvHistory.Items.Add(idle);
        }
        _lvHistory.EndUpdate();
    }

    private static bool ActiveRowEqual(ListViewItem row, ActiveDownload d)
    {
        var sub = row.SubItems;
        return sub.Count == 4 &&
               sub[0].Text == d.FileName &&
               sub[2].Text == ProgressText(d) &&
               row.Tag == d.Operation;
    }

    private static ListViewItem ActiveRow(ActiveDownload d)
    {
        var item = new ListViewItem(new[]
        {
            d.FileName,
            FormatSize(d.BytesReceived),
            ProgressText(d),
            ShortHost(d.Url)
        })
        {
            Tag = d.Operation,
            ForeColor = Brand.TextMain
        };
        item.SubItems[2].ForeColor = Brand.Blue;
        return item;
    }

    private static ListViewItem HistoryRow(DownloadItem d)
    {
        var item = new ListViewItem(new[]
        {
            d.FileName,
            d.State,
            FormatSize(d.TotalBytes),
            d.When.ToString("MM-dd HH:mm"),
            ShortHost(d.Url)
        })
        {
            Tag = d,
            ForeColor = d.State == "已完成" ? Brand.TextMain : Brand.TextDim
        };
        item.SubItems[1].ForeColor = d.State == "已完成"
            ? Color.FromArgb(0x1B, 0x8A, 0x4B)
            : Brand.TextDim;
        item.ToolTipText = d.Url + "\n" + d.FilePath;
        return item;
    }

    private static string ProgressText(ActiveDownload d)
    {
        if (d.Percent < 0) return Lang.T("dl.progressUnknown", FormatSize(d.BytesReceived));
        return $"{d.Percent}% · {FormatSize(d.BytesReceived)} / {FormatSize(d.TotalBytes)}";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    private static string ShortHost(string url)
    {
        try { return new Uri(url).Host; } catch { return ""; }
    }

    // ────────────────────────────── 动作 ──────────────────────────────

    private DownloadItem? SelectedHistory()
    {
        if (_lvHistory.SelectedItems.Count == 0) return null;
        return _lvHistory.SelectedItems[0].Tag as DownloadItem;
    }

    private ActiveDownload? SelectedActive()
    {
        if (_lvActive.SelectedItems.Count == 0) return null;
        var op = _lvActive.SelectedItems[0].Tag as Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation;
        if (op is null) return null;
        return DownloadStore.ActiveList.FirstOrDefault(d => ReferenceEquals(d.Operation, op));
    }

    private void OpenSelectedFile()
    {
        var d = SelectedHistory();
        if (d is null)
        {
            MessageBox.Show(this, Lang.T("dl.pickHistory"), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!DownloadStore.OpenFile(d))
            MessageBox.Show(this, Lang.T("dl.missingFile", d.FilePath), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OpenSelectedFolder()
    {
        var d = SelectedHistory();
        if (d is null)
        {
            MessageBox.Show(this, Lang.T("dl.pickHistory"), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DownloadStore.OpenFolder(d);
    }

    private void CancelSelected()
    {
        var d = SelectedActive();
        if (d is null)
        {
            MessageBox.Show(this, Lang.T("dl.pickActive"), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        d.Cancel();
    }

    private void RemoveSelected()
    {
        var d = SelectedHistory();
        if (d is null)
        {
            MessageBox.Show(this, Lang.T("dl.pickHistory"), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DownloadStore.Remove(d);
    }

    private void ClearHistory()
    {
        if (DownloadStore.HistoryCount == 0) return;
        var answer = MessageBox.Show(this,
            Lang.T("dl.clearAsk"),
            Lang.T("dl.clearTitle", Brand.AppName),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        DownloadStore.Clear();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosing(e);
    }
}
