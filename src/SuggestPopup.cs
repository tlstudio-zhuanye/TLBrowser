using System.Drawing.Drawing2D;

namespace TLBrowser;

internal enum SuggestKind
{
    /// <summary>输入本身就是地址，回车直接去。</summary>
    Navigate,
    /// <summary>公司官网直达。</summary>
    Brand,
    /// <summary>历史记录。</summary>
    History,
    /// <summary>交给搜索。</summary>
    Search
}

internal sealed class SuggestRow
{
    public SuggestKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string Hint { get; init; } = "";
    /// <summary>选中后要用的值：地址类给 URL，搜索类给关键词。</summary>
    public string Value { get; init; } = "";
}

/// <summary>
/// 地址栏联想下拉。
///
/// 关键点是它绝对不能抢焦点：抢了焦点，地址栏里的 TextBox 就会失焦，
/// 失焦就会触发「收起下拉」，于是光标一动下拉就自己消失了。
/// 所以走 ShowWithoutActivation + WS_EX_NOACTIVATE，让它「浮在上面但不当前台窗口」。
/// </summary>
internal sealed class SuggestPopup : Form
{
    private const int RowHeight = 34;
    private const int PadY = 5;
    private const int Radius = 10;

    private readonly Font _textFont = new("Microsoft YaHei UI", 9f);
    private readonly Font _hintFont = new("Microsoft YaHei UI", 8.25f);
    private readonly Font _glyphFont = new("Segoe MDL2 Assets", 9.5f);

    private List<SuggestRow> _rows = new();
    private int _selected = -1;
    private int _hover = -1;

    public event Action<SuggestRow>? Chosen;

    public SuggestPopup(Form owner)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Owner = owner;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;    // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080;    // WS_EX_TOOLWINDOW：不出现在 Alt+Tab 里
            return cp;
        }
    }

    public int SelectedIndex => _selected;

    public SuggestRow? Current =>
        _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

    /// <summary>展开在 anchor 的正下方。anchor 是屏幕坐标（地址栏的下边缘）。</summary>
    public void ShowRows(IReadOnlyList<SuggestRow> rows, Rectangle anchor, int width)
    {
        if (rows.Count == 0) { Dismiss(); return; }

        _rows = rows.ToList();
        _hover = -1;
        _selected = 0;

        width = Math.Max(220, width);
        var height = _rows.Count * RowHeight + PadY * 2;

        // 别捅出屏幕下沿：位置不够就往上翻到锚点上方
        var screen = Screen.FromPoint(new Point(anchor.Left, anchor.Top)).WorkingArea;
        var y = anchor.Bottom + 2;
        if (y + height > screen.Bottom)
            y = Math.Max(screen.Top, anchor.Top - height - 6);

        var x = Math.Min(anchor.Left, Math.Max(screen.Left, screen.Right - width));

        SetBounds(x, y, width, height);

        try
        {
            Region = new Region(Ui.Rounded(new Rectangle(0, 0, width, height), Radius));
        }
        catch { }

        if (!Visible) Show();
        Invalidate();
    }

    public void Dismiss()
    {
        if (IsHandleCreated && Visible) Hide();
        _rows = new List<SuggestRow>();
        _selected = -1;
        _hover = -1;
    }

    /// <summary>
    /// 上下键移动选择，到头就停住（不回绕，回绕容易选错）。
    /// 名字不叫 Move 是因为 Form 已经有一个 Move 事件，重名会把它遮掉。
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _rows.Count - 1);
        Invalidate();
    }

    public void Commit()
    {
        var row = Current;
        if (row is null) return;
        Dismiss();
        Chosen?.Invoke(row);
    }

    // ────────────────────────────── 绘制 ──────────────────────────────

    private static (string glyph, Color color) Style(SuggestKind kind) => kind switch
    {
        SuggestKind.Navigate => ("\uE71B", Brand.Blue),
        SuggestKind.Brand => ("\uE734", Brand.Purple),
        SuggestKind.History => ("\uE81C", Color.FromArgb(0x88, 0x87, 0x80)),
        _ => ("\uE721", Color.FromArgb(0x6B, 0x74, 0x80))
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(Color.White))
            g.FillRectangle(bg, ClientRectangle);

        for (var i = 0; i < _rows.Count; i++)
        {
            var y = PadY + i * RowHeight;
            var rect = new Rectangle(1, y, Width - 2, RowHeight);

            if (i == _selected || i == _hover)
            {
                using var fill = new SolidBrush(i == _selected
                    ? Color.FromArgb(0xEA, 0xF4, 0xFB)
                    : Color.FromArgb(0xF2, 0xF5, 0xF8));
                g.FillRectangle(fill, rect);
            }

            var row = _rows[i];
            var (glyph, color) = Style(row.Kind);

            // 选中项左边留一根品牌色细条，比只改底色更容易一眼看到
            if (i == _selected)
            {
                using var bar = new SolidBrush(color);
                g.FillRectangle(bar, 0, y + 6, 3, RowHeight - 12);
            }

            TextRenderer.DrawText(g, glyph, _glyphFont,
                new Rectangle(14, y, 20, RowHeight), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 右侧提示先按需要量出来，主文案才知道自己能占多宽
            var hintWidth = 0;
            if (row.Hint.Length > 0 && Width > 380)
            {
                hintWidth = Math.Min(Width / 2, TextRenderer.MeasureText(row.Hint, _hintFont).Width + 8);
                TextRenderer.DrawText(g, row.Hint, _hintFont,
                    new Rectangle(Width - hintWidth - 12, y, hintWidth, RowHeight),
                    Color.FromArgb(0xA6, 0xAE, 0xB8),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            var textRect = new Rectangle(42, y,
                Math.Max(20, Width - 42 - hintWidth - 16), RowHeight);
            TextRenderer.DrawText(g, row.Text, _textFont, textRect,
                Brand.TextMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        using var border = new Pen(Brand.ChromeLine, 1f);
        g.DrawPath(border, Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Radius));
    }

    // ────────────────────────────── 鼠标 ──────────────────────────────

    private int RowAt(Point p)
    {
        var i = (p.Y - PadY) / RowHeight;
        if (p.Y < PadY || i < 0 || i >= _rows.Count) return -1;
        return i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = RowAt(e.Location);
        if (i == _hover) return;
        _hover = i;
        _selected = i >= 0 ? i : _selected;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var i = RowAt(e.Location);
        if (i < 0) return;
        _selected = i;
        Commit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textFont.Dispose();
            _hintFont.Dispose();
            _glyphFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
