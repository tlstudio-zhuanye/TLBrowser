using System.Drawing.Drawing2D;

namespace TLBrowser;

/// <summary>
/// 自绘标签条：圆角标签、favicon、加载动画、关闭按钮、+ 新建。
///
/// 三个容易踩的点：
///  1. 标签放不下时不能像以前那样「画不出来的就 break」——那样标签会静默失踪。
///     改成保留最小宽度 + 横向滚动，滚轮在溢出时才生效。
///  2. 拖动重排用「实时回流」：光标越过半个槽位就把元素换位，松手才提交给主窗口。
///     这样视觉上跟得上手，也不用在松手瞬间做一次突兀的跳变。
///  3. 所有坐标都要减去 _scroll。漏一处就会出现「看得见点不中」。
/// </summary>
internal sealed class TabStrip : Control
{
    private const int TabHeight = 32;
    private const int TopPad = 5;
    private const int PlusWidth = 34;
    private const int MinTabWidth = 96;
    private const int MaxTabWidth = 210;
    private const int LeftPad = 5;

    private readonly List<WebTab> _tabs = new();
    private readonly System.Windows.Forms.Timer _spinner;
    private int _phase;
    private int _hoverIndex = -1;
    private bool _hoverClose;
    private bool _hoverPlus;
    private WebTab? _selected;

    private int _scroll;
    private bool _dragging;
    private int _dragIndex = -1;
    private Point _downPoint;

    private readonly Font _titleFont = new("Microsoft YaHei UI", 9f);
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 10f);
    private readonly Font _closeFont = new("Segoe MDL2 Assets", 7.5f);

    public event Action<WebTab>? SelectRequested;
    public event Action<WebTab>? CloseRequested;
    public event Action<WebTab>? ContextRequested;
    public event Action? NewTabRequested;

    /// <summary>拖动重排结束后，把新顺序整体交出去（顺序由本控件先行调整）。</summary>
    public event Action<IReadOnlyList<WebTab>>? OrderChanged;

    public TabStrip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Brand.ChromeBg;
        Height = TopPad + TabHeight + 3;
        Dock = DockStyle.Top;

        _spinner = new System.Windows.Forms.Timer { Interval = 60 };
        _spinner.Tick += (_, _) =>
        {
            _phase = (_phase + 30) % 360;
            if (_tabs.Any(t => t.IsLoading)) Invalidate();
        };
    }

    public WebTab? Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public IReadOnlyList<WebTab> Tabs => _tabs;

    public void Sync()
    {
        if (!_tabs.Any(t => t.IsLoading)) _spinner.Stop();
        else if (!_spinner.Enabled) _spinner.Start();
        Invalidate();
    }

    public void Add(WebTab tab)
    {
        _tabs.Add(tab);
        ClampScroll();
        Invalidate();
    }

    public void Remove(WebTab tab)
    {
        var i = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (_hoverIndex == i) _hoverIndex = -1;
        ClampScroll();
        Invalidate();
    }

    // ────────────────────────────── 布局 ──────────────────────────────

    private (int width, int step) Metrics()
    {
        var n = Math.Max(1, _tabs.Count);
        var avail = Math.Max(0, Width - LeftPad * 2 - PlusWidth);
        var w = Math.Clamp(avail / n, MinTabWidth, MaxTabWidth);
        return (w, w + 3);
    }

    /// <summary>内容总宽（所有标签 + 新建按钮 + 右边距）。</summary>
    private int ContentWidth()
    {
        var (_, step) = Metrics();
        return LeftPad + _tabs.Count * step + 2 + 26 + LeftPad;
    }

    private int MaxScroll() => Math.Max(0, ContentWidth() - Width);

    /// <summary>能滚才滚；不能滚时一律归零，免得留下一个「看着没动但坐标全偏」的状态。</summary>
    private void ClampScroll()
    {
        var max = MaxScroll();
        var next = Math.Clamp(_scroll, 0, max);
        if (next != _scroll) _scroll = next;
    }

    private int HitTest(Point p, out bool onClose, out bool onPlus)
    {
        onClose = false;
        onPlus = false;

        if (!_dragging)
        {
            var plusRect = PlusRect();
            if (plusRect.Contains(p)) { onPlus = true; return -1; }
        }

        for (var i = 0; i < _tabs.Count; i++)
        {
            var rect = TabRect(i);
            if (!rect.Contains(p)) continue;
            var close = CloseRect(rect);
            onClose = !_dragging && close.Contains(p);
            return i;
        }
        return -1;
    }

    private Rectangle PlusRect()
    {
        var (_, step) = Metrics();
        return new Rectangle(LeftPad + _tabs.Count * step + 2 - _scroll, TopPad + 4, 26, 24);
    }

    private static Rectangle CloseRect(Rectangle tab) =>
        new(tab.Right - 24, tab.Y + (tab.Height - 18) / 2, 18, 18);

    private Rectangle TabRect(int i)
    {
        var (w, step) = Metrics();
        return new Rectangle(LeftPad + i * step - _scroll, TopPad, w, TabHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    // ────────────────────────────── 绘制 ──────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        for (var i = 0; i < _tabs.Count; i++)
        {
            var rect = TabRect(i);
            if (rect.Right < 0) continue;
            if (rect.X > Width) break;
            DrawTab(g, _tabs[i], rect, i);
        }

        DrawPlus(g);
        DrawScrollHints(g);
    }

    private void DrawTab(Graphics g, WebTab tab, Rectangle rect, int index)
    {
        var active = ReferenceEquals(tab, _selected);
        var hovered = index == _hoverIndex;
        var dragging = _dragging && index == _dragIndex;

        if (active || hovered || dragging)
        {
            using var path = RoundedRect(rect, 9);
            using var fill = new SolidBrush(active ? Brand.TabActiveBg : Brand.TabHoverBg);
            g.FillPath(fill, path);
            if (active)
            {
                using var stroke = new Pen(Brand.ChromeLine, 1f);
                g.DrawPath(stroke, path);

                // 顶部品牌色条（裁进标签圆角里，别糊出去）
                var prev = g.Save();
                g.SetClip(path);
                using var accent = new SolidBrush(Brand.Blue);
                g.FillRectangle(accent, rect.X, rect.Y, rect.Width, 3);
                g.Restore(prev);
            }
            if (dragging)
            {
                using var ring = new Pen(Ui.Mix(Brand.Blue, Color.White, 0.35), 1f);
                g.DrawPath(ring, path);
            }
        }

        // 拖动中把标题压淡一点，读起来像「这张牌被拿起来了」
        var titleColor = active && !dragging ? Brand.TextMain : Brand.TextDim;

        // 图标区：加载中转圈，否则 favicon
        var iconRect = new Rectangle(rect.X + 10, rect.Y + (rect.Height - 16) / 2, 16, 16);
        if (tab.IsLoading) DrawSpinner(g, iconRect);
        else if (tab.Favicon is not null)
        {
            try { g.DrawImage(tab.Favicon, iconRect); } catch { DrawGlobe(g, iconRect); }
        }
        else DrawGlobe(g, iconRect);

        // 标题
        var titleRect = new Rectangle(iconRect.Right + 7, rect.Y,
            Math.Max(10, rect.Right - iconRect.Right - 7 - 26), rect.Height);
        TextRenderer.DrawText(g, tab.Title, _titleFont, titleRect,
            titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // 关闭按钮（当前标签或悬停时显示；拖动整排的时候不显示，免得误点）
        if (_dragging) return;

        if (active || (hovered && _hoverClose))
        {
            var close = CloseRect(rect);
            if (hovered && _hoverClose)
            {
                using var bg = new SolidBrush(Color.FromArgb(224, 84, 84));
                g.FillEllipse(bg, close);
                TextRenderer.DrawText(g, "\uE8BB", _closeFont, close, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(g, "\uE8BB", _closeFont, close, Brand.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private void DrawPlus(Graphics g)
    {
        if (_dragging) return;
        var rect = PlusRect();
        if (rect.X > Width) return;
        if (_hoverPlus)
        {
            using var path = RoundedRect(rect, 7);
            using var fill = new SolidBrush(Brand.TabHoverBg);
            g.FillPath(fill, path);
        }
        TextRenderer.DrawText(g, "\uE710", _iconFont, rect,
            _hoverPlus ? Brand.Blue : Brand.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>溢出时在两端画一点渐隐，提示「后面还有标签」。</summary>
    private void DrawScrollHints(Graphics g)
    {
        if (MaxScroll() <= 0) return;

        const int w = 22;
        if (_scroll > 0)
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(0, TopPad, w, TabHeight), Ui.Alpha(Brand.ChromeBg, 235),
                Ui.Alpha(Brand.ChromeBg, 0), 0f);
            g.FillRectangle(brush, 0, TopPad, w, TabHeight);
        }
        if (_scroll < MaxScroll())
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(Width - w, TopPad, w, TabHeight), Ui.Alpha(Brand.ChromeBg, 0),
                Ui.Alpha(Brand.ChromeBg, 235), 0f);
            g.FillRectangle(brush, Width - w, TopPad, w, TabHeight);
        }
    }

    private void DrawSpinner(Graphics g, Rectangle rect)
    {
        var pen = new Pen(Brand.Blue, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var r = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
        g.DrawArc(pen, r, _phase, 250);
        pen.Dispose();
    }

    private void DrawGlobe(Graphics g, Rectangle rect)
    {
        using var pen = new Pen(Color.FromArgb(0xB4, 0xB2, 0xA9), 1.2f);
        var r = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
        g.DrawEllipse(pen, r);
        g.DrawEllipse(pen, new Rectangle(r.X + r.Width / 3, r.Y, r.Width / 3, r.Height));
        g.DrawLine(pen, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ────────────────────────────── 交互 ──────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            DragTo(e.Location);
            return;
        }

        var index = HitTest(e.Location, out var onClose, out var onPlus);
        if (index != _hoverIndex || onClose != _hoverClose || onPlus != _hoverPlus)
        {
            _hoverIndex = index;
            _hoverClose = onClose;
            _hoverPlus = onPlus;
            Cursor = onPlus || index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging) return;                 // 拖出边界不该把拖动状态清掉
        _hoverIndex = -1;
        _hoverClose = false;
        _hoverPlus = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var index = HitTest(e.Location, out var onClose, out var onPlus);

        if (e.Button == MouseButtons.Middle)
        {
            if (index >= 0) CloseRequested?.Invoke(_tabs[index]);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (onPlus) { NewTabRequested?.Invoke(); return; }
        if (index < 0) return;

        var tab = _tabs[index];
        if (onClose) { CloseRequested?.Invoke(tab); return; }

        SelectRequested?.Invoke(tab);

        // 记下按下位置，够远才开始拖——否则普通点击也会被判成拖动
        _dragIndex = index;
        _downPoint = e.Location;
        Capture = true;
    }

    /// <summary>按下之后移动超过 6px 才算拖动，避免和「点一下切换标签」抢手势。</summary>
    private void DragTo(Point p)
    {
        if (!_dragging)
        {
            if (Math.Abs(p.X - _downPoint.X) < 6 && Math.Abs(p.Y - _downPoint.Y) < 6) return;
            _dragging = true;
            Cursor = Cursors.SizeWE;
        }

        var (_, step) = Metrics();
        if (step <= 0) return;

        var slot = (int)Math.Floor((p.X - LeftPad + _scroll + step / 2.0) / step);
        var target = Math.Clamp(slot, 0, _tabs.Count - 1);
        if (target == _dragIndex) { Invalidate(); return; }

        var moving = _tabs[_dragIndex];
        _tabs.RemoveAt(_dragIndex);
        _tabs.Insert(target, moving);
        _dragIndex = target;
        _hoverIndex = target;
        ClampScroll();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Right && !_dragging)
        {
            var index = HitTest(e.Location, out _, out _);
            if (index >= 0) ContextRequested?.Invoke(_tabs[index]);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        Capture = false;

        if (_dragging)
        {
            _dragging = false;
            Cursor = Cursors.Default;
            _dragIndex = -1;
            Invalidate();
            OrderChanged?.Invoke(_tabs.ToArray());
            return;
        }

        _dragIndex = -1;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var index = HitTest(e.Location, out _, out var onPlus);
        if (e.Button == MouseButtons.Left && index < 0 && !onPlus) NewTabRequested?.Invoke();
    }

    /// <summary>标签溢出时滚轮横向滚动；没溢出就不响应（交给系统滚动更好）。</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var max = MaxScroll();
        if (max <= 0) { base.OnMouseWheel(e); return; }

        var (_, step) = Metrics();
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * Math.Max(60, step), 0, max);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spinner.Dispose();
            _titleFont.Dispose();
            _iconFont.Dispose();
            _closeFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
