namespace TLBrowser;

/// <summary>
/// 企业模式设置窗。两种形态：
///   未启用 —— 填锁定页 + 设管理员密码 → 启用；
///   已启用 —— 先输密码解锁，才看得见网址、改密码、关闭企业模式。
/// 密码只在本窗口里出现一次，落盘的是 PBKDF2 摘要（见 Kiosk）。
/// </summary>
internal sealed class KioskForm : Form
{
    private readonly Label _lblState = new()
    {
        AutoSize = false,
        ForeColor = Brand.TextDim,
        Font = new Font("Microsoft YaHei UI", 9f),
        BackColor = Color.Transparent
    };

    private readonly Label _lblUrl = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Brand.TextMain,
        BackColor = Color.Transparent
    };

    private readonly TextBox _txtUrl = new()
    {
        Font = new Font("Microsoft YaHei UI", 10f),
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly Label _lblPass = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Brand.TextMain,
        BackColor = Color.Transparent
    };

    private readonly TextBox _txtPass = new()
    {
        Font = new Font("Microsoft YaHei UI", 10f),
        BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = true
    };

    private readonly Label _lblPass2 = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Brand.TextMain,
        BackColor = Color.Transparent
    };

    private readonly TextBox _txtPass2 = new()
    {
        Font = new Font("Microsoft YaHei UI", 10f),
        BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = true
    };

    private readonly CheckBox _chkSameHost = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Brand.TextMain,
        BackColor = Color.White,
        Checked = true
    };

    private readonly Label _lblHint = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 8.75f),
        ForeColor = Brand.TextDim,
        BackColor = Color.Transparent
    };

    private readonly PillButton _btnPrimary;
    private readonly PillButton _btnSecondary;
    private readonly PillButton _btnClose;

    /// <summary>已启用模式下，密码校验通过之后才能动设置。</summary>
    private bool _unlocked;

    /// <summary>关掉之后主窗口要按新的锁定状态重新导航所有标签。</summary>
    public bool SettingsChanged { get; private set; }

    public KioskForm()
    {
        Text = Lang.T("kiosk.window", Brand.AppName);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 400);
        MinimizeBox = false;
        MaximizeBox = false;
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

        _btnPrimary = new PillButton("", Brand.Blue, true);
        _btnPrimary.Click += (_, _) => Primary();

        _btnSecondary = new PillButton(Lang.T("kiosk.disable"), Brand.Purple, false);
        _btnSecondary.Click += (_, _) => DisableKiosk();

        _btnClose = new PillButton(Lang.T("kiosk.cancel"), Brand.Blue, false);
        _btnClose.Click += (_, _) => Close();

        var title = new Label
        {
            AutoSize = false,
            Text = Lang.T("kiosk.title"),
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Brand.TextMain,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(22, 18, 400, 28)
        };

        Controls.Add(title);
        Controls.Add(_lblState);
        Controls.Add(_lblUrl);
        Controls.Add(_txtUrl);
        Controls.Add(_lblPass);
        Controls.Add(_txtPass);
        Controls.Add(_lblPass2);
        Controls.Add(_txtPass2);
        Controls.Add(_chkSameHost);
        Controls.Add(_lblHint);
        Controls.Add(_btnPrimary);
        Controls.Add(_btnSecondary);
        Controls.Add(_btnClose);

        Layout();
        RefreshState();
    }

    private new void Layout()
    {
        var w = ClientSize.Width;
        const int left = 22;
        var fieldW = w - left * 2;

        _lblState.SetBounds(left, 50, fieldW, 20);
        _lblUrl.SetBounds(left, 86, fieldW, 18);
        _txtUrl.SetBounds(left, 106, fieldW, 30);
        _lblPass.SetBounds(left, 150, fieldW, 18);
        _txtPass.SetBounds(left, 170, (fieldW - 14) / 2, 30);
        _lblPass2.SetBounds(left + (fieldW - 14) / 2 + 14, 150, (fieldW - 14) / 2, 18);
        _txtPass2.SetBounds(left + (fieldW - 14) / 2 + 14, 170, (fieldW - 14) / 2, 30);
        _chkSameHost.SetBounds(left, 214, fieldW, 22);
        _lblHint.SetBounds(left, 244, fieldW, 48);

        const int bh = 32, by = 312;
        _btnPrimary.SetBounds(left, by, 132, bh);
        _btnSecondary.SetBounds(left + 140, by, 150, bh);
        _btnClose.SetBounds(w - left - 88, by, 88, bh);
    }

    /// <summary>按当前状态决定哪些控件能用、按钮上写什么。</summary>
    private void RefreshState()
    {
        var on = Kiosk.Enabled;
        var locked = on && !_unlocked;

        _lblState.Text = on ? Lang.T("kiosk.stateOn", Kiosk.Url) : Lang.T("kiosk.stateOff");
        _lblUrl.Text = Lang.T("kiosk.urlLabel");
        _chkSameHost.Text = Lang.T("kiosk.sameHost");
        _lblHint.Text = Lang.T("kiosk.hint");

        _txtUrl.Text = on ? Kiosk.Url : "";
        _txtUrl.Enabled = !locked;
        _chkSameHost.Enabled = !locked;
        _chkSameHost.Checked = Kiosk.AllowSameHost;

        // 未启用：密码 + 确认（初次设定）
        // 已启用未解锁：只要一个密码框，用来解锁
        // 已解锁：两个框变成「新密码 / 确认」，留空表示不改密码
        var wantSecond = !on || _unlocked;
        _lblPass.Text = (on && _unlocked) ? Lang.T("kiosk.newPassLabel") : Lang.T("kiosk.passLabel");
        _lblPass2.Text = Lang.T("kiosk.pass2Label");
        _lblPass2.Visible = _txtPass2.Visible = wantSecond;
        _txtPass.Text = "";
        _txtPass2.Text = "";

        _btnPrimary.Text = on ? (_unlocked ? Lang.T("kiosk.save") : Lang.T("kiosk.unlock"))
                              : Lang.T("kiosk.enable");
        _btnSecondary.Visible = on && _unlocked;
    }

    private void Primary()
    {
        if (!Kiosk.Enabled)
        {
            var url = _txtUrl.Text.Trim();
            if (url.Length == 0)
            {
                Warn(Lang.T("kiosk.needUrl"));
                return;
            }
            var p1 = _txtPass.Text;
            var p2 = _txtPass2.Text;
            if (p1.Length == 0)
            {
                Warn(Lang.T("kiosk.needPass"));
                return;
            }
            if (!string.Equals(p1, p2, StringComparison.Ordinal))
            {
                Warn(Lang.T("kiosk.passMismatch"));
                return;
            }

            try
            {
                Kiosk.Enable(url, p1);
                Kiosk.SetAllowSameHost(_chkSameHost.Checked);
            }
            catch
            {
                Warn(Lang.T("kiosk.needUrl"));
                return;
            }

            SettingsChanged = true;
            Info(Lang.T("kiosk.enabled", Kiosk.Url));
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (!_unlocked)
        {
            if (!Kiosk.Verify(_txtPass.Text))
            {
                Warn(Lang.T("kiosk.badPass"));
                return;
            }
            _unlocked = true;
            RefreshState();
            return;
        }

        // 已解锁 → 保存（密码框留空表示不改密码）
        var newUrl = _txtUrl.Text.Trim();
        if (newUrl.Length == 0)
        {
            Warn(Lang.T("kiosk.needUrl"));
            return;
        }

        var np1 = _txtPass.Text;
        var np2 = _txtPass2.Text;
        if (np1.Length > 0 && !string.Equals(np1, np2, StringComparison.Ordinal))
        {
            Warn(Lang.T("kiosk.passMismatch"));
            return;
        }

        try
        {
            Kiosk.SetUrl(newUrl);
            Kiosk.SetAllowSameHost(_chkSameHost.Checked);
            if (np1.Length > 0) Kiosk.SetPassword(np1);
        }
        catch
        {
            Warn(Lang.T("kiosk.needUrl"));
            return;
        }

        SettingsChanged = true;
        Info(np1.Length > 0 ? Lang.T("kiosk.passChanged") : Lang.T("kiosk.saved"));
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DisableKiosk()
    {
        Kiosk.Disable();
        SettingsChanged = true;
        Info(Lang.T("kiosk.disabled"));
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string text) =>
        MessageBox.Show(this, text, Brand.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void Info(string text) =>
        MessageBox.Show(this, text, Brand.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
