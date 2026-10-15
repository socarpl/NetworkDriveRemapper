using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper;

/// <summary>The explicit recovery action selected after Windows reports credential conflict 1219.</summary>
public enum CredentialConflictResolution
{
    /// <summary>Abandon mapping and close the mapping editor back to the drive grid.</summary>
    Cancel,
    /// <summary>Retry with the single known login already connected to the target server.</summary>
    UseExistingLogin,
    /// <summary>Force-disconnect the reviewed server connections after confirmation, then retry original credentials.</summary>
    DisconnectAndRetry
}

/// <summary>Reviews current-user server connections and offers the three requested 1219 recovery choices.</summary>
public sealed class CredentialConflictDialog : Form
{
    private readonly ServerConnectionReport report;
    private readonly Func<IWin32Window, string, bool> confirmDisconnect;

    /// <summary>Creates the modal connection review with a Yes/No confirmation defaulting to No.</summary>
    public CredentialConflictDialog(string errorMessage, ServerConnectionReport report)
        : this(errorMessage, report, ConfirmDisconnect) { }

    /// <summary>Builds the same UI with a confirmation callback for safe non-destructive regression checks.</summary>
    internal CredentialConflictDialog(string errorMessage, ServerConnectionReport report,
        Func<IWin32Window, string, bool> confirmDisconnect)
    {
        this.report = report;
        this.confirmDisconnect = confirmDisconnect;
        Text = "Network login conflict — Windows error 1219";
        Name = nameof(CredentialConflictDialog);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 660);
        MinimumSize = new Size(950, 620);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.White;
        BuildLayout(errorMessage);
    }

    /// <summary>Gets the chosen recovery action; closing the window or pressing Escape cancels the whole operation.</summary>
    public CredentialConflictResolution Resolution { get; private set; } = CredentialConflictResolution.Cancel;

    /// <summary>Displays the original error, reported login, server-wide connections, and three explicit choices.</summary>
    private void BuildLayout(string errorMessage)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 112, 66, 28 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 76, 38, 48 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label
        {
            Text = "This server is already connected with another login", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.FromArgb(26, 47, 73)
        }, 0, 0);
        layout.Controls.Add(new TextBox
        {
            Name = "CredentialConflictErrorText", Text = errorMessage.ReplaceLineEndings("\r\n"), Dock = DockStyle.Fill,
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Color.Firebrick, ScrollBars = ScrollBars.Vertical, TabStop = false
        }, 0, 1);
        string[] known = report.Connections.Select(connection => connection.Login).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string loginText = report.ExistingLogin ?? (known.Length > 0
            ? string.Join(", ", known) + " (no single active login could be selected)" : "Not reported by Windows");
        layout.Controls.Add(new Label
        {
            Name = "ExistingServerLoginLabel", Dock = DockStyle.Fill,
            Text = $"Server: \\\\{report.Server}\nLogin in use: {loginText}\nWindows cannot retrieve the existing password; reusing the login uses its established session.",
            ForeColor = Color.FromArgb(39, 48, 60)
        }, 0, 2);
        layout.Controls.Add(new Label { Text = "Connections to this server in your current Windows logon", Dock = DockStyle.Fill }, 0, 3);

        var grid = new DataGridView
        {
            Name = "ServerConnectionsGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            BackgroundColor = Color.White, AutoGenerateColumns = false
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Drive", Width = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Resource", HeaderText = "Server / share", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Login", HeaderText = "Login in use", Width = 210 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ConnectionStatus", HeaderText = "Status", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "References", HeaderText = "Open references", Width = 140 });
        foreach (DataGridViewColumn column in grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (NetworkConnection connection in report.Connections)
            grid.Rows.Add(connection.LocalName ?? "—", connection.RemotePath, connection.Login ?? "Not reported",
                connection.Status, connection.OpenCount?.ToString() ?? "Not reported");
        layout.Controls.Add(grid, 0, 4);

        string notes = report.Warnings.Count > 0 ? string.Join("\n", report.Warnings)
            : "The list includes connections without drive letters. Reusing the login keeps other server connections in place.\nDisconnecting affects every listed share on this server, not just the proposed target.";
        if (!report.CanDisconnect)
            notes += "\nBulk disconnection is unavailable because the list is empty or incomplete.";
        if (report.ExistingLogin is null)
            notes += "\nReusing an existing login is unavailable because Windows did not report one unambiguous active account.";
        layout.Controls.Add(new TextBox
        {
            Name = "ConnectionDiscoveryNotes", Text = notes.ReplaceLineEndings("\r\n"), Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Color.DimGray,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false
        }, 0, 5);
        layout.Controls.Add(new Label
        {
            Text = "Choose how you would like to continue", Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
        }, 0, 6);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Button existing = CreateButton("UseExistingLoginButton", "use existing login instead", 225);
        existing.Enabled = report.ExistingLogin is not null;
        existing.Click += (_, _) => Complete(CredentialConflictResolution.UseExistingLogin);
        Button disconnect = CreateButton("DisconnectServerButton", "drop all connections and retry with provided credentials", 425);
        disconnect.Enabled = report.CanDisconnect;
        disconnect.Click += (_, _) =>
        {
            if (confirmDisconnect(this, BuildConfirmationText()))
                Complete(CredentialConflictResolution.DisconnectAndRetry);
            // No leaves this same modal review open with no network operation performed.
        };
        Button cancel = CreateButton("CancelConflictButton", "Cancel operations", 170);
        cancel.Click += (_, _) => Complete(CredentialConflictResolution.Cancel);
        buttons.Controls.AddRange([existing, disconnect, cancel]);
        layout.Controls.Add(buttons, 0, 7);
        CancelButton = cancel;
        Controls.Add(layout);
    }

    /// <summary>Builds a consistent action button with a stable control name.</summary>
    private static Button CreateButton(string name, string text, int width) => new()
    {
        Name = name, Text = text, Size = new Size(width, 36), Margin = new Padding(0, 5, 8, 5)
    };

    /// <summary>Explains the scope and possible loss of unsaved work before any forced disconnection.</summary>
    private string BuildConfirmationText()
    {
        string affected = string.Join("\n", report.Connections.Select(connection =>
            $"• {connection.LocalName ?? "No drive letter"}: {connection.RemotePath} — {connection.Login ?? "Login not reported"}"));
        return $"All listed connections to \\\\{report.Server} in your current Windows logon will be forcibly disconnected.\n\n"
            + "Unsaved changes may be lost. Open documents, running applications, file transfers, and background tasks using these shares may fail or be interrupted. Save your work and close files before continuing.\n\n"
            + "Listed drive mappings, including their remembered mappings, will be removed. Disconnected connections cannot be automatically restored because Windows does not return their passwords. Another application may reconnect afterward. Connections to other named servers and other users' logon sessions are outside this action.\n\n"
            + $"Affected connections:\n{affected}\n\nDo you really want to proceed?";
    }

    /// <summary>Shows an owned Yes/No warning and defaults to No so Enter cannot accidentally force a drop.</summary>
    private static bool ConfirmDisconnect(IWin32Window owner, string text) => MessageBox.Show(owner, text,
        "Confirm disconnecting server connections", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Stores the requested action and closes this review; network effects are performed by the mapping service.</summary>
    private void Complete(CredentialConflictResolution resolution)
    {
        Resolution = resolution;
        DialogResult = resolution == CredentialConflictResolution.Cancel ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}
