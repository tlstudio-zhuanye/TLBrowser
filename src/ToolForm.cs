namespace TLBrowser;

/// <summary>
/// 工具箱的通用窗口。一个窗子顶三种用法：
///   输入型 —— 上面一个多行输入框，下面执行按钮；
///   结果型 —— 上面只读结果，下面复制按钮；
///   输入 + 结果 —— 上下各半，点「执行」当场出结果，不用开第二个窗口。
///
/// 之所以做成通用的：100 项功能如果各配一个窗口，光界面代码就能把项目撑爆，
/// 而且以后加功能还得再写一个。这里只做一套壳，功能只管算。
/// </summary>
internal sealed class ToolForm : Form
{
    private readonly TextBox _input;
    private readonly TextBox _output;
    private readonly Label _hint;
    private readonly Button? _btnRun;
    private readonly Button _btnCopy;
    private readonly Button _btnClear;
    private readonly Button _btnClose;
    private readonly bool _hasOutput;

    /// <summary>点「执行」时回调。返回要显示的结果文本。</summary>
    private Func<string, Task<string>>? _run;
    private bool _busy;

    /// <summary>
    /// 构造。input 为 true 时显示输入框，output 为 true 时显示结果框。
    /// 两个都为 true 就是「输入 → 执行 → 结果」那种。
    /// </summary>
    public ToolForm(string title, string? hint, bool input, bool output)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = true;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);
        MinimumSize = new Size(520, 320);

        _hasOutput = output;
        var hasInput = input;

        // 尺寸按用途给：纯出结果的窗口做矮一点，别没事占半屏
        ClientSize = output && !input ? new Size(720, 460)
                   : input && output ? new Size(720, 560)
                   : new Size(720, 420);

        var top = 12;

        _hint = new Label
        {
            Bounds = new Rectangle(18, top, ClientSize.Width - 36, 20),
            Text = hint ?? "",
            ForeColor = Brand.TextDim,
            AutoSize = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Visible = !string.IsNullOrEmpty(hint)
        };
        if (_hint.Visible) top += 26;

        const int pad = 18;
        var bottomBar = 50;
        var boxW = ClientSize.Width - pad * 2;

        _input = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10f),
            BorderStyle = BorderStyle.FixedSingle,
            AcceptsReturn = true,
            AcceptsTab = true
        };

        _output = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(0xFA, 0xFB, 0xFC),
            ForeColor = Brand.TextMain
        };

        // 输入输出都有的场合，上下各占一半；只有一边就整块给它
        var usable = ClientSize.Height - top - bottomBar - pad;
        if (hasInput && output)
        {
            var half = (usable - 10) / 2;
            _input.SetBounds(pad, top, boxW, half);
            _output.SetBounds(pad, top + half + 10, boxW, half);
        }
        else if (hasInput)
        {
            _input.SetBounds(pad, top, boxW, usable);
        }
        else
        {
            _output.SetBounds(pad, top, boxW, usable);
        }

        var bx = pad;
        var by = ClientSize.Height - bottomBar + 10;

        if (hasInput)
        {
            _btnRun = new Button
            {
                Text = Lang.T("toolbox.run"),
                Bounds = new Rectangle(bx, by, 92, 30),
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            _btnRun.Click += async (_, _) => await RunAsync();
            AcceptButton = _btnRun;
            bx += 100;
        }

        _btnCopy = new Button
        {
            Text = Lang.T("toolbox.copy"),
            Bounds = new Rectangle(bx, by, 100, 30),
            FlatStyle = FlatStyle.System,
            Enabled = output,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        _btnCopy.Click += (_, _) =>
        {
            try
            {
                if (_output.TextLength > 0) Clipboard.SetText(_output.Text);
            }
            catch { }
        };

        _btnClear = new Button
        {
            Text = Lang.T("toolbox.clear"),
            Bounds = new Rectangle(bx + 108, by, 72, 30),
            FlatStyle = FlatStyle.System,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        _btnClear.Click += (_, _) =>
        {
            if (hasInput) _input.Clear();
            if (output) _output.Clear();
            if (hasInput) _input.Focus();
        };

        _btnClose = new Button
        {
            Text = Lang.T("toolbox.close"),
            Size = new Size(84, 30),
            FlatStyle = FlatStyle.System,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            DialogResult = DialogResult.Cancel
        };
        _btnClose.Location = new Point(ClientSize.Width - pad - 84, by);
        CancelButton = _btnClose;

        Controls.AddRange(new Control[] { _hint, _input, _output, _btnCopy, _btnClear, _btnClose });
        if (_btnRun is not null) Controls.Add(_btnRun);

        // 输入型的窗口直接把光标放进去，点开就能打字
        if (hasInput) Shown += (_, _) => _input.Focus();
    }

    public ToolForm(string title, string body) : this(title, null, false, true) => SetResult(body);

    /// <summary>给结果框塞内容。构造完之后调。</summary>
    public void SetResult(string text)
    {
        _output.Text = text.Replace("\n", Environment.NewLine);
        _output.SelectionStart = 0;
    }

    /// <summary>给输入框预设内容（比如把当前网址带进来）。</summary>
    public void SetInput(string text)
    {
        _input.Text = text;
        _input.SelectionStart = _input.TextLength;
    }

    /// <summary>挂上「执行」回调。挂上之后按钮才真的能用。</summary>
    public void OnRun(Func<string, Task<string>> run) => _run = run;

    /// <summary>取当前输入框内容（外部要拿它当参数时用）。</summary>
    public string InputText => _input.Text;

    private async Task RunAsync()
    {
        if (_busy || _run is null) return;

        if (_input.TextLength == 0)
        {
            _output.Text = Lang.T("toolbox.empty");
            return;
        }

        _busy = true;
        if (_btnRun is not null) _btnRun.Enabled = false;
        try
        {
            var result = await _run(_input.Text);
            SetResult(result);
        }
        catch (Exception ex)
        {
            SetResult(Lang.T("toolbox.error") + ex.Message);
        }
        finally
        {
            _busy = false;
            if (_btnRun is not null) _btnRun.Enabled = true;
        }
    }
}
