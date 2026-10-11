namespace TLBrowser;

/// <summary>
/// 企业模式设置窗（网站白名单）。两种形态：
///   未启用 —— 编名单 + 设管理员密码 → 启用；
///   已启用 —— 先输密码解锁，才改得动名单、密码，或者关掉企业模式。
/// 名单第一条是启动页。密码只在本窗口里出现一次，落盘的是 PBKDF2 摘要（见 Kiosk）。
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

    private readonly Label _lblList = new()
    {
        AutoSize = false,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Brand.TextMain,
        BackColor = Color.Transparent
    };

    private readonly ListBox _lstAllowed = new()
    {
        Font = new Font("Microsoft YaHei UI", 9.5f),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
        ForeColor = Brand.TextMain,
        IntegralHeight = false,
        ScrollAlwaysVisible = true
    };

    private readonly TextBox _txtAdd = new()
    {
        Font = new Font("Microsoft YaHei UI", 9.5f),
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

    private readonly PillButton _btnAdd;
    private readonly PillButton _btnRemove;
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
        ClientSize = new Size(620, 540);
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

        _btnAdd = new PillButton(Lang.T("kiosk.add"), Brand.Blue, false);
        _btnAdd.Click += (_, _) => AddSite();

        _btnRemove = new PillButton(Lang.T("kiosk.remove"), Brand.Purple, false);
        _btnRemove.Click += (_, _) => RemoveSite();

        _btnPrimary = new PillButton("", Brand.Blue, true);
        _btnPrimary.Click += (_, _) => Primary();

        _btnSecondary = new PillButton(Lang.T("kiosk.disable"), Brand.Purple, false);
        _btnSecondary.Click += (_, _) => DisableKiosk();

        _btnClose = new PillButton(Lang.T("kiosk.cancel"), Brand.Blue, false);
        _btnClose.Click += (_, _) => Close();

        // 输入框里按回车就当点「添加」，省得每次去够鼠标
        _txtAdd.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddSite();
        };

        var title = new Label
        {
            AutoSize = false,
            Text = Lang.T("kiosk.title"),
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = Brand.TextMain,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(22, 18, 520, 28)
        };

        Controls.Add(title);
        Controls.Add(_lblState);
        Controls.Add(_lblList);
        Controls.Add(_lstAllowed);
        Controls.Add(_txtAdd);
        Controls.Add(_btnAdd);
        Controls.Add(_btnRemove);
        Controls.Add(_lblPass);
        Controls.Add(_txtPass);
        Controls.Add(_lblPass2);
        Controls.Add(_txtPass2);
        Controls.Add(_chkSameHost);
        Controls.Add(_lblHint);
        Controls.Add(_btnPrimary);
        Controls.Add(_btnSecondary);
        Controls.Add(_btnClose);

        // 已有配置先灌进名单（未启用时 Kiosk.Allowed 是空的，名单就是个草稿本）
        foreach (var s in Kiosk.Allowed) _lstAllowed.Items.Add(s);

        Layout();
        RefreshState();
    }

    private new void Layout()
    {
        var w = ClientSize.Width;
        const int left = 22;
        var fieldW = w - left * 2;

        _lblState.SetBounds(left, 52, fieldW, 34);
        _lblList.SetBounds(left, 94, fieldW, 18);
        _lstAllowed.SetBounds(left, 114, fieldW, 156);

        const int rowY = 278, rowH = 30, btnW = 80;
        _btnRemove.SetBounds(left + fieldW - btnW, rowY, btnW, rowH);
        _btnAdd.SetBounds(left + fieldW - btnW - 8 - 68, rowY, 68, rowH);
        _txtAdd.SetBounds(left, rowY, fieldW - btnW - 8 - 68 - 8, rowH);

        var half = (fieldW - 14) / 2;
        _lblPass.SetBounds(left, 322, half, 18);
        _lblPass2.SetBounds(left + half + 14, 322, half, 18);
        _txtPass.SetBounds(left, 342, half, 30);
        _txtPass2.SetBounds(left + half + 14, 342, half, 30);

        _chkSameHost.SetBounds(left, 384, fieldW, 22);
        _lblHint.SetBounds(left, 412, fieldW, 52);

        const int bh = 32, by = 480;
        _btnPrimary.SetBounds(left, by, 132, bh);
        _btnSecondary.SetBounds(left + 140, by, 150, bh);
        _btnClose.SetBounds(w - left - 88, by, 88, bh);
    }

    /// <summary>按当前状态决定哪些控件能用、按钮上写什么。名单本身不重建（它是当前的草稿）。</summary>
    private void RefreshState()
    {
        var on = Kiosk.Enabled;
        var locked = on && !_unlocked;

        _lblState.Text = on ? Lang.T("kiosk.stateOn", Kiosk.Count) : Lang.T("kiosk.stateOff");
        _lblList.Text = Lang.T("kiosk.listLabel");
        _chkSameHost.Text = Lang.T("kiosk.sameHost");
        _lblHint.Text = Lang.T("kiosk.hint");
        _txtAdd.PlaceholderText = Lang.T("kiosk.addHint");

        _lstAllowed.Enabled = !locked;
        _txtAdd.Enabled = !locked;
        _btnAdd.Enabled = !locked;
        _btnRemove.Enabled = !locked;
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

    /// <summary>把输入框里的那条加进名单（规范化 + 去重）。</summary>
    private void AddSite()
    {
        if (_txtAdd.Text.Trim().Length == 0) return;

        var url = Kiosk.NormalizeUrl(_txtAdd.Text);
        if (url.Length == 0)
        {
            Warn(Lang.T("kiosk.badUrl"));
            return;
        }

        foreach (var o in _lstAllowed.Items)
            if (string.Equals(o as string, url, StringComparison.OrdinalIgnoreCase))
            {
                Warn(Lang.T("kiosk.dupUrl"));
                return;
            }

        _lstAllowed.Items.Add(url);
        _txtAdd.Text = "";
        _lstAllowed.SelectedIndex = _lstAllowed.Items.Count - 1;
    }

    private void RemoveSite()
    {
        var i = _lstAllowed.SelectedIndex;
        if (i < 0) return;
        _lstAllowed.Items.RemoveAt(i);
        if (_lstAllowed.Items.Count > 0)
            _lstAllowed.SelectedIndex = Math.Min(i, _lstAllowed.Items.Count - 1);
    }

    /// <summary>把名单读出来（界面上已经是规范化过的地址了）。</summary>
    private List<string> Collect()
    {
        var list = new List<string>();
        foreach (var o in _lstAllowed.Items)
        {
            var s = o as string ?? "";
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    private void Primary()
    {
        if (!Kiosk.Enabled)
        {
            var sites = Collect();
            if (sites.Count == 0)
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
                Kiosk.Enable(sites, p1);
                Kiosk.SetAllowSameHost(_chkSameHost.Checked);
            }
            catch
            {
                Warn(Lang.T("kiosk.needUrl"));
                return;
            }

            SettingsChanged = true;
            Info(Lang.T("kiosk.enabled", Kiosk.Count));
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
        var newSites = Collect();
        if (newSites.Count == 0)
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
            Kiosk.SetAllowed(newSites);
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
