using System.Drawing.Drawing2D;

namespace TLBrowser;

internal static class Ui
{
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>换透明度。Color 自己没有这个方法，画渐隐遮罩时到处都要用。</summary>
    public static Color Alpha(Color c, int a) =>
        Color.FromArgb(Math.Clamp(a, 0, 255), c);
}

/// <summary>工具栏上的方形图标按钮（前进/后退/刷新/主页/菜单）。</summary>
internal sealed class IconButton : Button
{
    private readonly string _normalTip;

    public IconButton(string glyph, string tip, float glyphSize = 11f)
    {
        _normalTip = tip;
        Text = glyph;
        Font = new Font("Segoe MDL2 Assets", glyphSize);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Brand.TabHoverBg;
        FlatAppearance.MouseDownBackColor = Brand.ChromeLine;
        BackColor = Brand.ChromeBg;
        ForeColor = Brand.TextMain;
        TabStop = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, false);
        new ToolTip().SetToolTip(this, tip);
    }

    public void SetTip(string tip) => new ToolTip().SetToolTip(this, tip);

    /// <summary>刷新按钮在「加载中」时变成停止按钮。</summary>
    public string Glyph
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ForeColor = Enabled ? Brand.TextMain : Color.FromArgb(0xC4, 0xCA, 0xD1);
        BackColor = Brand.ChromeBg;
    }
}

/// <summary>品牌直达按钮（药丸形）。Filled = 实心品牌色，否则描边幽灵按钮。</summary>
internal sealed class PillButton : Control
{
    private readonly Font _font = new("Microsoft YaHei UI", 9f);
    private bool _hover;
    private bool _pressed;

    public Color Accent { get; set; } = Brand.Blue;
    public bool Filled { get; set; } = true;
    public string Tip { get; set; } = "";

    public PillButton(string text, Color accent, bool filled)
    {
        Accent = accent;
        Filled = filled;
        Text = text;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Brand.ChromeBg;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, (Height - 30) / 2, Width - 1, 29);
        using var path = Ui.Rounded(rect, 15);

        if (Filled)
        {
            var fill = _pressed ? Ui.Mix(Accent, Color.Black, 0.18)
                     : _hover ? Ui.Mix(Accent, Color.Black, 0.08)
                     : Accent;
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, _font, rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            using var b = new SolidBrush(_hover ? Ui.Mix(Accent, Color.White, 0.88) : Color.White);
            g.FillPath(b, path);
            using var pen = new Pen(_hover ? Accent : Brand.ChromeLine, 1f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, _font, rect, Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>圆角地址栏：左边一个状态图标，右边输入框。</summary>
internal sealed class AddressBar : Panel
{
    private readonly Label _glyph = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe MDL2 Assets", 9.5f),
        ForeColor = Brand.TextDim,
        BackColor = Color.Transparent
    };

    public TextBox Input { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        Font = new Font("Microsoft YaHei UI", 9.5f),
        ForeColor = Brand.TextMain,
        BackColor = Color.White
    };

    /// <summary>
    /// 自己画占位提示。TextBox.PlaceholderText 走的是系统 cue banner，
    /// 控件一拿到焦点就消失；地址栏启动即聚焦，等于永远看不到提示。
    /// </summary>
    private readonly Label _hint = new()
    {
        AutoSize = false,
        Text = Lang.T("sug.placeholder"),
        ForeColor = Color.FromArgb(0xA6, 0xAE, 0xB8),
        BackColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 9.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        Cursor = Cursors.IBeam
    };

    private bool _focus;

    /// <summary>
    /// 用户自己点了地址栏（而不是程序把它设焦）。
    /// 主窗口拿这个信号决定「现在可以弹联想下拉了」——启动时那个自动聚焦不算。
    /// </summary>
    public event Action? UserClicked;

    public AddressBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Brand.ChromeBg;
        Controls.Add(_glyph);
        Controls.Add(Input);
        Controls.Add(_hint);
        _hint.BringToFront();          // 必须盖在输入框上面才看得见

        Input.GotFocus += (_, _) => { _focus = true; Invalidate(); };
        Input.LostFocus += (_, _) => { _focus = false; Invalidate(); };
        Input.TextChanged += (_, _) => { SyncHint(); Invalidate(); };
        Input.MouseDown += (_, _) => UserClicked?.Invoke();
        _hint.MouseDown += (_, _) => { UserClicked?.Invoke(); Input.Focus(); };

        SyncHint();
    }

    private void SyncHint() => _hint.Visible = Input.TextLength == 0;

    public string Glyph
    {
        get => _glyph.Text;
        set => _glyph.Text = value;
    }

    public Color GlyphColor
    {
        get => _glyph.ForeColor;
        set => _glyph.ForeColor = value;
    }

    public void FocusInput()
    {
        Input.Focus();
        Input.SelectAll();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_hint is null || _glyph is null || Input is null) return;   // 基类构造期可能先触发一次
        var r = ClientRectangle;
        var h = Math.Max(20, r.Height - 2);
        _glyph.SetBounds(9, 1, 22, h);
        var inputLeft = _glyph.Right + 2;
        var inputWidth = Math.Max(40, r.Width - _glyph.Right - 18);
        Input.SetBounds(inputLeft, 1 + (h - Input.PreferredHeight) / 2, inputWidth, Input.PreferredHeight);

        // 从输入框左边进去 3px，让输入光标（第 0 列）露出来，别被提示文字压住
        _hint.SetBounds(inputLeft + 3, Input.Top, Math.Max(20, inputWidth - 3), Input.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, (Height - 30) / 2, Width - 1, 29);
        using var path = Ui.Rounded(rect, 15);
        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, path);

        var borderColor = _focus ? Brand.Blue : Brand.ChromeLine;
        using var pen = new Pen(borderColor, _focus ? 1.6f : 1f);
        g.DrawPath(pen, path);

        base.OnPaint(e);
    }
}

/// <summary>
/// 状态条右侧的主页守护指示器。正常时是蓝色盾牌，
/// 一旦拦下过东西就变琥珀色并把条数写出来——不然用户根本不知道它在工作。
/// </summary>
internal sealed class ShieldButton : Control
{
    private readonly Font _font = new("Microsoft YaHei UI", 8.25f);
    private readonly Font _glyphFont = new("Segoe MDL2 Assets", 9.5f);
    private readonly ToolTip _tip = new();
    private bool _hover;
    private Color _accent = Brand.Blue;
    private int _handled;
    private int _attention;

    public ShieldButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Brand.StatusBg;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    /// <summary>
    /// handled = 本次自动拦下并还原的条数；attention = 系统层面需要注意的条数。
    /// 两者都为 0 才是蓝色的「正常」。
    /// </summary>
    public void SetState(int handled, int attention)
    {
        if (_handled == handled && _attention == attention) return;
        _handled = handled;
        _attention = attention;
        _accent = handled > 0 || attention > 0 ? Color.FromArgb(0xC0, 0x6A, 0x00) : Brand.Blue;
        Invalidate();
    }

    private string StatusText => _handled > 0 ? Lang.T("shield.handledShort", _handled)
        : _attention > 0 ? Lang.T("shield.todoShort", _attention)
        : Lang.T("shield.normalShort");

    public string Tip
    {
        set => _tip.SetToolTip(this, value);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hover)
        {
            using var path = Ui.Rounded(new Rectangle(0, 2, Width - 1, Math.Max(6, Height - 5)), 4);
            using var b = new SolidBrush(Brand.TabHoverBg);
            g.FillPath(b, path);
        }

        TextRenderer.DrawText(g, "\uEA18", _glyphFont,
            new Rectangle(4, 0, 18, Height), _accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, StatusText, _font,
            new Rectangle(24, 0, Math.Max(20, Width - 28), Height),
            _hover ? Brand.TextMain : Brand.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _font.Dispose(); _glyphFont.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>底部状态条：左边状态文字，右边缩放比例，加载时顶部跑一条进度光带。</summary>
internal sealed class StatusBarPanel : Panel
{
    private readonly System.Windows.Forms.Timer _timer;
    private int _offset;
    private bool _busy;

    public StatusBarPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Brand.StatusBg;

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) =>
        {
            _offset = (_offset + 14) % Math.Max(40, Width + 200);
            Invalidate();
        };
    }

    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value) return;
            _busy = value;
            if (_busy) { _offset = -160; _timer.Start(); }
            else _timer.Stop();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_busy) return;

        var g = e.Graphics;
        var seg = Math.Max(120, Width / 4);
        var x = _offset - seg;
        g.SetClip(new Rectangle(0, 0, Width, 3));
        using var brush = new LinearGradientBrush(
            new Rectangle(x, 0, seg, 3), Color.FromArgb(0, Brand.Blue), Brand.Blue, 0f);
        g.FillRectangle(brush, x, 0, seg, 3);
        g.ResetClip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
