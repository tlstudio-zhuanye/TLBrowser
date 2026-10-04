using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TLBrowser;

/// <summary>
/// 启动动画（FL Studio 风格）：深色深渊里亮起 TL DOUBLER STUDIO 标，
/// 蓄力一下之后被“扔进深渊”——透视缩小远去，光轨加速掠过，冲击波闪光，
/// 最后淡出并把已经加载好的主窗口交出来。点一下或按任意键可跳过。
/// </summary>
internal sealed class SplashForm : Form
{
    private const int W = 780;
    private const int H = 450;
    private const double TotalMs = 2280;

    // 时间轴（毫秒）
    private const double TAppear = 110;     // logo 开始出现
    private const double TLogoFull = 880;   // logo 完全显现
    private const double TChargeEnd = 1180; // 蓄力结束，开始被扔出去
    private const double TThrowEnd = 1930;  // 已消失在深渊深处
    private const int StreakCount = 110;

    private sealed class Streak
    {
        public double Angle;
        public double Dist;
        public double Speed;
        public double Len;
        public float Width;
        public int Tint;
    }

    private readonly Image? _logo;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Streak> _streaks = new();
    private readonly Random _rng = new(20261004);
    private readonly Font _footerFont = new("Microsoft YaHei UI", 9f);
    private readonly Font _hintFont = new("Microsoft YaHei UI", 8.25f);

    private double _offset;
    private double _lastMs;
    private double _rush = 1.0;
    private bool _finished;

    private static readonly Color Cyan = Color.FromArgb(0x38, 0xE1, 0xFF);
    private static readonly Color Blue = Color.FromArgb(0x1D, 0x9C, 0xD8);
    private static readonly Color Pale = Color.FromArgb(0xD8, 0xF6, 0xFF);

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(W, H);
        ShowInTaskbar = false;
        TopMost = true;
        // 无边框窗口的标题不会显示，但留着方便外部按标题找到它（验收脚本用）
        Text = Lang.T("boot.splash", Brand.AppName);
        BackColor = Color.FromArgb(5, 8, 12);
        KeyPreview = true;
        Cursor = Cursors.Hand;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _logo = Brand.LoadImage(Brand.ResLogoDoubler);

        try { Region = new Region(Ui.Rounded(new Rectangle(0, 0, W, H), 18)); }
        catch { }

        SeedStreaks();
        _timer.Tick += OnTick;
    }

    private void SeedStreaks()
    {
        for (var i = 0; i < StreakCount; i++)
        {
            _streaks.Add(new Streak
            {
                Angle = _rng.NextDouble() * Math.PI * 2,
                Dist = 40 + _rng.NextDouble() * 900,
                Speed = 130 + _rng.NextDouble() * 420,
                Len = 18 + _rng.NextDouble() * 90,
                Width = 1f + (float)_rng.NextDouble() * 1.7f,
                Tint = _rng.Next(3)
            });
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _lastMs = 0;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalMilliseconds + _offset;
        var dt = Math.Max(0, Math.Min(0.1, (now - _lastMs) / 1000.0));
        _lastMs = now;

        // 被扔出去之后光轨整体加速，制造下坠感
        _rush = now <= TChargeEnd
            ? 1.0
            : 1.0 + 3.2 * Smooth((now - TChargeEnd) / (TThrowEnd - TChargeEnd));

        foreach (var s in _streaks)
        {
            s.Dist += s.Speed * _rush * dt;
            if (s.Dist > 1600)
            {
                s.Dist = 30 + _rng.NextDouble() * 70;
                s.Angle = _rng.NextDouble() * Math.PI * 2;
                s.Speed = 130 + _rng.NextDouble() * 420;
                s.Len = 18 + _rng.NextDouble() * 90;
            }
        }

        if (now >= TotalMs) { Finish(); return; }

        var remain = TotalMs - now;
        Opacity = remain < 240 ? Math.Max(0.0, remain / 240) : 1.0;
        Invalidate();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Close();
    }

    private void Skip()
    {
        var now = _clock.Elapsed.TotalMilliseconds + _offset;
        var target = TotalMs - 340;
        if (now < target) _offset += target - now;
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Skip(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); Skip(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var now = _clock.Elapsed.TotalMilliseconds + _offset;
        var intro = Clamp01(now / 320);
        var cx = W / 2.0;
        var cy = H / 2.0 - 26;

        DrawAbyss(g, cx, cy, intro);
        DrawStreaks(g, cx, cy, intro);
        DrawTunnelRings(g, cx, cy, intro, now);
        DrawLogo(g, cx, cy, now);
        DrawBurst(g, cx, cy, now);
        DrawFlash(g, now);
        DrawVignette(g, cx, cy);
        DrawExitGate(g, cx, cy, now);
        DrawFooter(g, now);

        using var frame = new Pen(Color.FromArgb(40, 56, 72));
        g.DrawPath(frame, Ui.Rounded(new Rectangle(0, 0, W - 1, H - 1), 18));
    }

    // ────────────────────────────── 画面元素 ──────────────────────────────

    private void DrawAbyss(Graphics g, double cx, double cy, double intro)
    {
        using (var bg = new SolidBrush(Color.FromArgb(5, 8, 12)))
            g.FillRectangle(bg, ClientRectangle);

        // 深渊深处的幽蓝辉光
        var r = (float)(300 + 200 * intro);
        using var glow = new GraphicsPath();
        glow.AddEllipse((float)(cx - r), (float)(cy - r * 0.7), r * 2, (float)(r * 1.4));
        using var pgb = new PathGradientBrush(glow)
        {
            CenterColor = Color.FromArgb((int)(80 * intro), 24, 92, 128),
            SurroundColors = new[] { Color.FromArgb(0, 5, 8, 12) }
        };
        g.FillPath(pgb, glow);
    }

    private void DrawStreaks(Graphics g, double cx, double cy, double intro)
    {
        foreach (var s in _streaks)
        {
            var ca = Math.Cos(s.Angle);
            var sa = Math.Sin(s.Angle);
            var len = s.Len * (0.5 + _rush * 0.5);

            var x1 = cx + ca * s.Dist;
            var y1 = cy + sa * s.Dist * 0.72;
            var x2 = cx + ca * (s.Dist + len);
            var y2 = cy + sa * (s.Dist + len) * 0.72;

            // 近处淡入、远处淡出，避免边缘突兀
            var a = 165 * intro * Clamp01(s.Dist / 200) * Clamp01(1 - s.Dist / 1500);
            if (_rush > 1) a *= 0.6 + 0.55 * Math.Min(1, _rush / 3.4);
            var alpha = (int)Math.Clamp(a, 0, 210);
            if (alpha <= 2) continue;

            var color = s.Tint switch
            {
                0 => Color.FromArgb(alpha, Cyan),
                1 => Color.FromArgb(alpha, Blue),
                _ => Color.FromArgb((int)(alpha * 0.75), Pale)
            };
            using var pen = new Pen(color, s.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, (float)x1, (float)y1, (float)x2, (float)y2);
        }
    }

    private void DrawTunnelRings(Graphics g, double cx, double cy, double intro, double now)
    {
        if (now <= TLogoFull) return;

        for (var i = 0; i < 4; i++)
        {
            var phase = ((now - TLogoFull) / 760.0 + i / 4.0) % 1.0;
            var rr = 80 + phase * 1050;
            var alpha = (int)(78 * (1 - phase) * intro);
            if (alpha <= 2) continue;

            using var pen = new Pen(Color.FromArgb(alpha, Cyan), 1.2f);
            g.DrawEllipse(pen, (float)(cx - rr), (float)(cy - rr * 0.72), (float)(rr * 2), (float)(rr * 1.44));
        }
    }

    private void DrawLogo(Graphics g, double cx, double cy, double now)
    {
        if (_logo is null) return;

        double scale, alpha, rot;
        if (now < TAppear) return;

        if (now <= TLogoFull)
        {
            var u = (now - TAppear) / (TLogoFull - TAppear);
            alpha = Smooth(u);
            scale = 1.0 - 0.07 * (1 - Smooth(u));
            rot = 0;
        }
        else if (now <= TChargeEnd)
        {
            var u = (now - TLogoFull) / (TChargeEnd - TLogoFull);
            alpha = 1;
            scale = 1.0 + 0.055 * Smooth(u);
            rot = 0;
        }
        else if (now <= TThrowEnd)
        {
            var u = (now - TChargeEnd) / (TThrowEnd - TChargeEnd);

            // 余晖拖影：再落后的两个时刻各画一份，越靠后越小越淡。
            // 单层直接缩小看起来是「瞬移」，加了两层残影才有「被甩出去」的速度感。
            for (var k = 2; k >= 1; k--)
            {
                var uu = Math.Max(0, u - 0.075 * k);
                var ee = uu * uu;
                BlitLogo(g, cx, cy,
                    1.055 * (1 - 0.97 * ee),
                    (1 - Math.Pow(uu, 0.7)) * (k == 1 ? 0.22 : 0.10),
                    -11 * ee);
            }

            var e = u * u;                       // 加速远去
            alpha = 1 - Math.Pow(u, 0.7);
            scale = 1.055 * (1 - 0.97 * e);
            rot = -11 * e;
        }
        else return;

        if (alpha <= 0.01 || scale <= 0.01) return;

        // 背后的品牌辉光：扔出去的瞬间最亮
        var glowBoost = now > TChargeEnd ? 1.5 : 1.0;
        DrawGlow(g, cx, cy, (float)(520 * 0.60 * scale), (int)(105 * alpha * glowBoost), Cyan);

        BlitLogo(g, cx, cy, scale, alpha, rot);
    }

    /// <summary>把 logo 以指定缩放/透明度/旋转画在中心。主图与残影共用。</summary>
    private void BlitLogo(Graphics g, double cx, double cy, double scale, double alpha, double rot)
    {
        if (_logo is null || alpha <= 0.01 || scale <= 0.01) return;

        const double lw = 520.0;
        var lh = lw * _logo.Height / _logo.Width;

        var state = g.Save();
        try
        {
            g.TranslateTransform((float)cx, (float)cy);
            if (Math.Abs(rot) > 0.01) g.RotateTransform((float)rot);
            g.ScaleTransform((float)scale, (float)scale);

            using var ia = new ImageAttributes();
            ia.SetColorMatrix(new ColorMatrix { Matrix33 = (float)alpha });
            g.DrawImage(_logo,
                new Rectangle((int)(-lw / 2), (int)(-lh / 2), (int)lw, (int)lh),
                0, 0, _logo.Width, _logo.Height, GraphicsUnit.Pixel, ia);
        }
        finally { g.Restore(state); }
    }

    private static void DrawGlow(Graphics g, double cx, double cy, float radius, int alpha, Color color)
    {
        if (radius <= 2 || alpha <= 2) return;
        using var path = new GraphicsPath();
        path.AddEllipse((float)(cx - radius), (float)(cy - radius * 0.62), radius * 2, radius * 1.24f);
        using var pgb = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(Math.Min(alpha, 255), color),
            SurroundColors = new[] { Color.FromArgb(0, color) }
        };
        g.FillPath(pgb, path);
    }

    /// <summary>投掷瞬间的冲击波圆环。</summary>
    private void DrawBurst(Graphics g, double cx, double cy, double now)
    {
        if (now <= TChargeEnd || now > TChargeEnd + 620) return;
        var u = (now - TChargeEnd) / 620.0;
        var rr = 60 + u * 780;
        var alpha = (int)(130 * (1 - u) * (1 - u));
        if (alpha <= 2) return;
        using var pen = new Pen(Color.FromArgb(alpha, Cyan), 2.4f);
        g.DrawEllipse(pen, (float)(cx - rr), (float)(cy - rr * 0.72), (float)(rr * 2), (float)(rr * 1.44));
    }

    private void DrawFlash(Graphics g, double now)
    {
        if (now <= TChargeEnd || now > TChargeEnd + 340) return;
        var u = (now - TChargeEnd) / 340.0;
        var alpha = (int)(135 * (1 - u) * (1 - u));
        if (alpha <= 2) return;
        using var brush = new SolidBrush(Color.FromArgb(alpha, 190, 245, 255));
        g.FillRectangle(brush, ClientRectangle);
    }

    private void DrawVignette(Graphics g, double cx, double cy)
    {
        using var path = new GraphicsPath();
        path.AddRectangle(ClientRectangle);
        using var pgb = new PathGradientBrush(path)
        {
            CenterPoint = new PointF((float)cx, (float)cy),
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(170, 0, 0, 0) }
        };
        g.FillPath(pgb, path);
    }

    /// <summary>
    /// 收尾的「光门」：logo 沉下去之后，一圈亮环从中心冲出去，
    /// 中心的幽蓝辉光同时涨起来。配合最后 240ms 的整窗淡出，
    /// 看起来像是从这道光里穿过去，接上已经加载好的主窗口。
    /// 不做纯白闪，那个太吵；这里只到「亮起来」为止。
    /// </summary>
    private void DrawExitGate(Graphics g, double cx, double cy, double now)
    {
        if (now <= TThrowEnd) return;

        var u = Clamp01((now - TThrowEnd) / (TotalMs - TThrowEnd));
        var e = u * u * (3 - 2 * u);

        var r = 60 + e * 760;
        var ring = (int)(150 * (1 - e) * (1 - e));
        if (ring > 3)
        {
            using var pen = new Pen(Color.FromArgb(ring, 226, 248, 255), 2f + (float)(e * 1.8));
            g.DrawEllipse(pen, (float)(cx - r), (float)(cy - r * 0.74),
                (float)(r * 2), (float)(r * 1.48));
        }

        DrawGlow(g, cx, cy, (float)(230 + e * 280), (int)(118 * e),
            Color.FromArgb(0xD8, 0xF2, 0xFF));
    }

    private void DrawFooter(Graphics g, double now)
    {
        var p = Clamp01(now / TotalMs);
        const int x = 40;
        var full = W - 80;
        var y = H - 58;

        using (var track = new SolidBrush(Color.FromArgb(22, 34, 46)))
            g.FillRectangle(track, x, y, full, 3);
        using (var fill = new SolidBrush(Blue))
            g.FillRectangle(fill, x, y, (int)(full * p), 3);
        // 进度条前端的亮点
        var knobX = (int)(x + full * p);
        using (var knob = new SolidBrush(Cyan))
            g.FillRectangle(knob, Math.Max(x, knobX - 14), y, 14, 3);

        // 一行两格：版本 / 跳过提示
        var row = new Rectangle(40, H - 44, W - 80, 24);

        TextRenderer.DrawText(g, $"TL 浏览器  v{Brand.AppVersion}", _footerFont,
            new Rectangle(row.X, row.Y, row.Width / 2, row.Height), Color.FromArgb(0x76, 0x86, 0x94),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        if (now < TotalMs - 400)
        {
            TextRenderer.DrawText(g, Lang.T("boot.skip"), _hintFont,
                new Rectangle(row.X + row.Width / 2, row.Y, row.Width / 2, row.Height),
                Color.FromArgb(0x44, 0x52, 0x60),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // ────────────────────────────── 小工具 ──────────────────────────────

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static double Smooth(double u) { u = Clamp01(u); return u * u * (3 - 2 * u); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _footerFont.Dispose();
            _hintFont.Dispose();
            _logo?.Dispose();
        }
        base.Dispose(disposing);
    }
}
