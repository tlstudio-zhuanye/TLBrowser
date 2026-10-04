using System.Diagnostics;

namespace TLBrowser;

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = Lang.T("about.title", Brand.AppName);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 306);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);

        var logo = new PictureBox
        {
            Bounds = new Rectangle(28, 24, 64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        try
        {
            var p = Path.Combine(Brand.HomeDir, "logo.png");
            if (File.Exists(p)) logo.Image = Image.FromFile(p);
        }
        catch { }

        var title = new Label
        {
            Bounds = new Rectangle(108, 30, 300, 30),
            Text = Brand.AppName,
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            ForeColor = Brand.TextMain
        };
        var version = new Label
        {
            Bounds = new Rectangle(110, 62, 300, 22),
            Text = "版本 " + Brand.AppVersion,
            ForeColor = Brand.TextDim
        };

        var accent = new Panel { Bounds = new Rectangle(28, 106, 374, 2), BackColor = Brand.ChromeLine };

        var link1 = MakeLink("tlstudio.cn", Brand.SiteTl, 178);
        var link2 = MakeLink("tldoublerstudio.cn", Brand.SiteDoubler, 204);

        var note = new Label
        {
            Bounds = new Rectangle(28, 124, 374, 42),
            Text = Lang.T("about.note"),
            ForeColor = Brand.TextDim
        };

        var ok = new Button
        {
            Text = Lang.T("common.ok"),
            Bounds = new Rectangle(318, 256, 84, 32),
            FlatStyle = FlatStyle.System,
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[] { logo, title, version, accent, note, link1, link2, ok });
        AcceptButton = ok;
    }

    private static LinkLabel MakeLink(string text, string url, int y)
    {
        var link = new LinkLabel
        {
            Bounds = new Rectangle(28, y, 374, 24),
            Text = text,
            LinkColor = Brand.Blue,
            ActiveLinkColor = Brand.BlueDark,
            VisitedLinkColor = Brand.Blue,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        };
        return link;
    }
}
