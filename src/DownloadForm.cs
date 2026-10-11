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

    // 切语言时要当场改写这几个控件的文字，所以得留引用
    private readonly Label _lblActive;
    private readonly Label _lblHistory;
    private readonly Label _lblTitle;
    private readonly Label _folderText = new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 8.75f),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };
    private readonly PillButton _btnChangeFolder;
    private readonly PillButton _btnResetFolder;

    private readonly PillButton _btnOpenFile;
    private readonly PillButton _btnOpenFolder;
    private readonly PillButton _btnCancel;
    private readonly PillButton _btnRemove;
    private readonly PillButton _btnClear;
    private readonly PillButton _btnClose;

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
        Text = Lang.T("dl.window", Brand.AppName);
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

        _lblTitle = new Label
        {
            AutoSize = false,
            Text = Lang.T("dl.title"),
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Brand.TextMain,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(66, 13, 300, 26)
        };

        _btnChangeFolder = new PillButton(Lang.T("dl.changeFolder"), Brand.Blue, false);
        _btnChangeFolder.Click += (_, _) => ChangeFolder();
        _btnResetFolder = new PillButton(Lang.T("dl.resetFolder"), Brand.Blue, false);
        _btnResetFolder.Click += (_, _) => ResetFolder();

        // 底部按钮也在这里 new：它们是 readonly 字段，只能在构造函数里赋值
        _btnOpenFile = new PillButton(Lang.T("dl.openFile"), Brand.Blue, true);
        _btnOpenFile.Click += (_, _) => OpenSelectedFile();
        _btnOpenFolder = new PillButton(Lang.T("dl.openFolder"), Brand.Blue, false);
        _btnOpenFolder.Click += (_, _) => OpenSelectedFolder();
        _btnCancel = new PillButton(Lang.T("dl.cancelSel"), Brand.Purple, false);
        _btnCancel.Click += (_, _) => CancelSelected();
        _btnRemove = new PillButton(Lang.T("dl.delete"), Brand.Purple, false);
        _btnRemove.Click += (_, _) => RemoveSelected();
        _btnClear = new PillButton(Lang.T("dl.clear"), Brand.Purple, false);
        _btnClear.Click += (_, _) => ClearHistory();
        _btnClose = new PillButton(Lang.T("dl.close"), Brand.Blue, false);
        _btnClose.Click += (_, _) => Close();

        var header = BuildHeader();
        _lblActive = MakeCaption(Lang.T("dl.active"));
        _lblHistory = MakeCaption(Lang.T("dl.history"));
        var folderBar = BuildFolderBar();
        var bar = BuildButtonBar();

        _lvActive.Height = 150;
        _lvHistory.Dock = DockStyle.Fill;

        // Dock 按倒序处理，Fill 的先加进来，否则会被 Top/Bottom 挤成 0 高
        Controls.Add(_lvHistory);
        Controls.Add(bar);
        Controls.Add(_lblHistory);
        Controls.Add(_lvActive);
        Controls.Add(_lblActive);
        Controls.Add(folderBar);
        Controls.Add(header);

        DownloadStore.Changed += () => _dirty = true;
        _timer.Tick += (_, _) => { if (_dirty) { _dirty = false; Reload(); } };
        _timer.Start();

        Reload();
    }

    /// <summary>
    /// 切换界面语言时由主窗口调用：本窗口是常驻单例，不重建，
    /// 所以标题、列头、按钮这些写在构造时的文字必须手动刷一遍。
    /// </summary>
    public void ApplyLanguage()
    {
        Text = Lang.T("dl.window", Brand.AppName);
        _lblTitle.Text = Lang.T("dl.title");
        _lblActive.Text = Lang.T("dl.active");
        _lblHistory.Text = Lang.T("dl.history");

        SetColumns(_lvActive,
            Lang.T("dl.col.file"), Lang.T("dl.col.size"),
            Lang.T("dl.col.progress"), Lang.T("dl.col.source"));
        SetColumns(_lvHistory,
            Lang.T("dl.col.file"), Lang.T("dl.col.status"), Lang.T("dl.col.size"),
            Lang.T("dl.col.time"), Lang.T("dl.col.source"));

        _btnOpenFile.Text = Lang.T("dl.openFile");
        _btnOpenFolder.Text = Lang.T("dl.openFolder");
        _btnCancel.Text = Lang.T("dl.cancelSel");
        _btnRemove.Text = Lang.T("dl.delete");
        _btnClear.Text = Lang.T("dl.clear");
        _btnClose.Text = Lang.T("dl.close");
        _btnChangeFolder.Text = Lang.T("dl.changeFolder");
        _btnResetFolder.Text = Lang.T("dl.resetFolder");

        // 行里的状态文字、占位行也是按语言生成的，清掉重画
        _lvActive.Items.Clear();
        _lvHistory.Items.Clear();
        Reload();
    }

    private static void SetColumns(ListView lv, params string[] texts)
    {
        for (var i = 0; i < lv.Columns.Count && i < texts.Length; i++)
            lv.Columns[i].Text = texts[i];
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

    /// <summary>下载位置条：左边显示当前存到哪，右边两个按钮改位置 / 恢复默认。</summary>
    private Control BuildFolderBar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Brand.StatusBg };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Brand.ChromeLine, 1f);
            e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };

        panel.Controls.Add(_folderText);
        panel.Controls.Add(_btnChangeFolder);
        panel.Controls.Add(_btnResetFolder);

        void Layout()
        {
            const int h = 26, y = 6, gap = 8, right = 12;
            var wChange = 104;
            var wReset = 88;
            var xReset = panel.ClientSize.Width - right - wReset;
            var xChange = xReset - gap - wChange;
            _btnChangeFolder.SetBounds(xChange, y, wChange, h);
            _btnResetFolder.SetBounds(xReset, y, wReset, h);
            var textW = Math.Max(80, xChange - gap - 18);
            _folderText.SetBounds(18, 0, textW, panel.Height);
        }

        panel.Resize += (_, _) => Layout();
        Layout();
        return panel;
    }

    /// <summary>换一个文件夹存新下载的文件。只影响之后的下载，已经下好的不动。</summary>
    private void ChangeFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Lang.T("dl.folderPickDesc"),
            UseDescriptionForTitle = true,
            SelectedPath = DownloadStore.DownloadFolder
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var picked = dlg.SelectedPath;
        if (string.IsNullOrWhiteSpace(picked)) return;
        try
        {
            DownloadStore.SetDownloadFolder(picked);
        }
        catch
        {
            MessageBox.Show(this, Lang.T("dl.folderBad", picked), Brand.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _dirty = true;
        Reload();
    }

    /// <summary>恢复成系统「下载」文件夹。</summary>
    private void ResetFolder()
    {
        DownloadStore.ResetDownloadFolder();
        _dirty = true;
        Reload();
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

        panel.Controls.Add(_btnOpenFile);
        panel.Controls.Add(_btnOpenFolder);
        panel.Controls.Add(_btnCancel);
        panel.Controls.Add(_btnRemove);
        panel.Controls.Add(_btnClear);
        panel.Controls.Add(_btnClose);

        panel.Resize += (_, _) =>
        {
            const int y = 13, h = 32;
            var x = 18;
            foreach (var (b, w) in new[] {
                (_btnOpenFile, 96), (_btnOpenFolder, 132), (_btnCancel, 120), (_btnRemove, 96), (_btnClear, 96)
            })
            {
                b.SetBounds(x, y, w, h);
                x += w + 8;
            }
            _btnClose.SetBounds(panel.ClientSize.Width - 92, y, 74, h);
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
        _status.Text = Lang.T(active > 0 ? "dl.sumActive" : "dl.sumIdle", active, hist);

        var folder = DownloadStore.DownloadFolder;
        _folderText.Text = Lang.T("dl.folderPrefix") + folder;
        _btnResetFolder.Enabled = DownloadStore.IsCustomFolder;
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
            var idle = new ListViewItem(new[] { Lang.T("dl.idleActive"), "—", "—", "—" });
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
            d.StateText,
            FormatSize(d.TotalBytes),
            d.When.ToString("MM-dd HH:mm"),
            ShortHost(d.Url)
        })
        {
            Tag = d,
            ForeColor = d.IsDone ? Brand.TextMain : Brand.TextDim
        };
        item.SubItems[1].ForeColor = d.IsDone
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
