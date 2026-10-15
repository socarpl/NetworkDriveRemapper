using System.Diagnostics;

namespace NetworkDriveRemapper;

/// <summary>Displays the original explanation of Everyone permissions, anonymous credentials, and Guest access.</summary>
public sealed class CredentialsHelpDialog : Form
{
    private readonly Action<Uri> openExternalLink;
    // Own the emphasis fonts once per dialog, rather than allocating one for each formatted range.
    private readonly Font boldFont;
    private readonly Font codeFont;

    /// <summary>Creates the scrollable explanation with documentation opened in the default external browser.</summary>
    public CredentialsHelpDialog() : this(OpenDefaultBrowser) { }

    /// <summary>Builds the same dialog with an injectable external opener for browser-free UI regression checks.</summary>
    /// <param name="openExternalLink">Receives the exact documentation URI when its LinkLabel is activated.</param>
    internal CredentialsHelpDialog(Action<Uri> openExternalLink)
    {
        this.openExternalLink = openExternalLink;
        Text = "Anonymous access and credentials";
        Name = nameof(CredentialsHelpDialog);
        Font = new Font("Segoe UI", 11);
        boldFont = new Font(Font, FontStyle.Bold);
        codeFont = new Font("Consolas", 11);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(790, 730);
        MinimumSize = new Size(640, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.White;
        BuildLayout();
    }

    /// <summary>Creates a readable heading, scrolling paragraphs, native hyperlinks, and a Close button.</summary>
    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(new Label
        {
            Text = "Anonymous access and credentials", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = Color.FromArgb(26, 47, 73)
        }, 0, 0);

        var scroll = new Panel { Name = "ExplanationScrollPanel", Dock = DockStyle.Fill, AutoScroll = true };
        var paragraphs = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(0, 0, 12, 0)
        };
        paragraphs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // The wording below is preserved verbatim from the explanation requested by the user.
        // Markdown emphasis becomes native text formatting; link captions and destinations are unchanged.
        AddParagraph(paragraphs, "EveryoneParagraph",
            "“Everyone” permissions do not automatically allow anonymous access. Windows normally excludes anonymous connections from the Everyone group. A share can therefore permit Everyone while still requiring a login or Guest identity.",
            ["“Everyone” permissions do not automatically allow anonymous access."], [],
            "Microsoft documentation",
            "https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-let-everyone-permissions-apply-to-anonymous-users");
        AddParagraph(paragraphs, "EmptyCredentialsParagraph",
            "In this app, Anonymous currently sends an explicitly empty username and password to the Windows connection API. That differs from letting Windows use your current account: default credentials require null values. The “Anonymous” label therefore simplifies several distinct Windows authentication behaviors.",
            ["Anonymous currently sends an explicitly empty username and password"], ["null"],
            "Windows API documentation",
            "https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w");
        AddParagraph(paragraphs, "GuestParagraph",
            "Arbitrary credentials succeeding suggests your server may be treating those connections as Guest. Windows has a “Guest only” sharing policy that maps local-account network logins to Guest. That can explain why a named login succeeds while an empty-credential attempt fails—but it remains a possible explanation until we see the error and server settings.",
            ["Arbitrary credentials succeeding suggests your server may be treating those connections as Guest."], [],
            "Guest-only policy documentation",
            "https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-sharing-and-security-model-for-local-accounts");
        AddParagraph(paragraphs, "ConnectionTestParagraph",
            "The test actually connects and attempts to list the directory. Success means the resulting session can access that directory; it does not necessarily establish that the entered username/password were accepted as that named account.",
            ["not necessarily establish that the entered username/password were accepted as that named account"], []);
        AddParagraph(paragraphs, "OriginalFollowUpParagraph",
            "What exact Windows error number/message appears when you test Anonymous? That will help distinguish an empty-username rejection from an anonymous-access or Guest-policy restriction. No files were changed.",
            [], []);
        scroll.Controls.Add(paragraphs);
        layout.Controls.Add(scroll, 0, 1);

        var close = new Button
        {
            Name = "CloseHelpButton", Text = "Close", Size = new Size(110, 34), Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.Cancel, Margin = new Padding(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close, 0, 2);
        AcceptButton = close;
        CancelButton = close;
        Controls.Add(layout);
    }

    /// <summary>Adds a paragraph with the original bold/code spans and optional clickable source caption.</summary>
    /// <param name="layout">Scrollable single-column paragraph layout.</param>
    /// <param name="name">Stable control name used for accessibility and content verification.</param>
    /// <param name="text">Original paragraph text without Markdown delimiters.</param>
    /// <param name="bold">Exact substrings originally emphasized in bold.</param>
    /// <param name="code">Exact substrings originally styled as inline code.</param>
    /// <param name="linkCaption">Original source-link caption, or null when the paragraph has no link.</param>
    /// <param name="linkUrl">Original external documentation URL, paired with the caption.</param>
    private void AddParagraph(TableLayoutPanel layout, string name, string text, string[] bold, string[] code,
        string? linkCaption = null, string? linkUrl = null)
    {
        var paragraph = new RichTextBox
        {
            Name = name, Text = text, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false,
            BackColor = Color.White, ForeColor = Color.FromArgb(39, 48, 60), Font = Font,
            Dock = DockStyle.Top, WordWrap = true, ScrollBars = RichTextBoxScrollBars.None,
            Margin = new Padding(0, 0, 0, linkCaption is null ? 18 : 4), Height = 120,
            AccessibleName = "Anonymous access explanation", TabStop = false
        };
        foreach (string span in bold)
            FormatSpan(paragraph, span, boldFont);
        foreach (string span in code)
            FormatSpan(paragraph, span, codeFont);
        paragraph.Select(0, 0);
        paragraph.ContentsResized += (_, args) => paragraph.Height = args.NewRectangle.Height + 6;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(paragraph, 0, layout.RowCount++);
        if (linkCaption is null || linkUrl is null)
            return;

        var link = new LinkLabel
        {
            Name = name + "DocumentationLink", Text = linkCaption, AutoSize = true,
            Margin = new Padding(0, 0, 0, 20), LinkBehavior = LinkBehavior.AlwaysUnderline,
            LinkColor = Color.FromArgb(24, 88, 166), ActiveLinkColor = Color.FromArgb(20, 60, 110)
        };
        link.Links.Clear();
        link.Links.Add(0, linkCaption.Length, new Uri(linkUrl));
        link.LinkClicked += (_, args) =>
        {
            if (args.Link?.LinkData is not Uri uri)
                return;
            try
            {
                openExternalLink(uri);
                args.Link.Visited = true;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Unable to open documentation", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(link, 0, layout.RowCount++);
    }

    /// <summary>Applies native formatting to an exact substring without changing the displayed wording.</summary>
    private static void FormatSpan(RichTextBox paragraph, string span, Font font)
    {
        int index = paragraph.Text.IndexOf(span, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("The requested emphasis text is missing from the explanation.");
        paragraph.Select(index, span.Length);
        paragraph.SelectionFont = font;
    }

    /// <summary>Asks Windows to open an HTTPS documentation URL in the configured external browser.</summary>
    private static void OpenDefaultBrowser(Uri uri)
    {
        using Process? browser = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Releases the fonts owned by the help dialog alongside its controls.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            boldFont.Dispose();
            codeFont.Dispose();
        }
    }
}
