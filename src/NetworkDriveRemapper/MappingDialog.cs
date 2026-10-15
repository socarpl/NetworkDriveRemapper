using System.ComponentModel;
using System.Security.Principal;
using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper;

/// <summary>Edits one letter's proposed UNC mapping, login mode, and Explorer preference.</summary>
public sealed class MappingDialog : Form
{
    // Secret values remain in the masked textbox and transient ConnectionOptions only.
    private readonly DriveMappingService service;
    private readonly DriveEntry entry;
    private readonly Func<string, ServerConnectionReport, CredentialConflictDialog> createConflictDialog;
    private readonly TextBox path = new();
    private readonly RadioButton anonymous = new();
    private readonly RadioButton credentials = new();
    private readonly TextBox username = new();
    private readonly TextBox password = new();
    private readonly CheckBox openAfterMapping = new();
    private readonly Button test = new();
    private readonly Button explorer = new();
    private readonly Button cancel = new();
    private readonly Button ok = new();
    private readonly Label status = new();
    private readonly LinkLabel readMore = new();
    private readonly ProgressBar progress = new();
    private bool busy;
    private bool useExistingLogin;

    /// <summary>Builds an editor using current-account credentials for a free letter or the existing mapped-drive defaults.</summary>
    /// <param name="service">Service used for test, browse, and commit operations.</param>
    /// <param name="entry">Immutable drive snapshot used to detect changes while the dialog is open.</param>
    public MappingDialog(DriveMappingService service, DriveEntry entry)
        : this(service, entry, (message, report) => new CredentialConflictDialog(message, report)) { }

    /// <summary>Builds the real mapping editor with an injectable recovery-dialog factory for non-destructive UI tests.</summary>
    internal MappingDialog(DriveMappingService service, DriveEntry entry,
        Func<string, ServerConnectionReport, CredentialConflictDialog> createConflictDialog)
    {
        this.service = service;
        this.entry = entry;
        this.createConflictDialog = createConflictDialog;
        Text = $"Map network drive — {entry.DeviceName}";
        Name = nameof(MappingDialog);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.White;
        BuildLayout();
        path.Text = entry.NetworkPath ?? string.Empty;
        if (entry.Kind == DriveKind.Free)
        {
            // The token reports DOMAIN\user (or COMPUTER\user for a local account); a workgroup is not an account domain.
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            username.Text = identity.Name;
            credentials.Checked = true;
        }
        else
        {
            anonymous.Checked = true;
        }
        openAfterMapping.Checked = true;
        UpdateCredentialControls();

        credentials.CheckedChanged += (_, _) => UpdateCredentialControls();
        path.TextChanged += (_, _) => ConnectionInputChanged();
        username.TextChanged += (_, _) => ConnectionInputChanged();
        password.TextChanged += (_, _) => ConnectionInputChanged();
        credentials.CheckedChanged += (_, _) => ConnectionInputChanged();
        test.Click += async (_, _) => await RunOperationAsync("Testing connection…", ReadOptions, options =>
        {
            service.TestConnection(options);
            return "Connection succeeded. The directory can be opened and listed.";
        }, recoverCredentialConflict: true);
        explorer.Click += async (_, _) => await RunOperationAsync("Opening location in Explorer…", () => path.Text, target =>
        {
            service.OpenLocation(target);
            return "The location was sent to Explorer. No drive letter was changed.";
        });
        ok.Click += async (_, _) => await ApplyMappingAsync();
        readMore.LinkClicked += (_, _) => ShowCredentialsHelp();
        cancel.Click += (_, _) =>
        {
            if (!busy)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        FormClosing += (_, args) => args.Cancel = busy;
        FormClosed += (_, _) => password.Clear();
        Shown += (_, _) => path.Focus();
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Creates target input, a mutually exclusive login group, status area, and four buttons.</summary>
    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 11
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 24, 36, 24, 64, 38, 38, 36 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = $"Network mapping for {entry.DeviceName}", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Color.FromArgb(26, 47, 73)
        }, 0, 0);
        layout.Controls.Add(new Label { Text = "Target path", Dock = DockStyle.Fill }, 0, 1);
        path.Name = "TargetPathTextBox";
        path.Dock = DockStyle.Fill;
        path.PlaceholderText = @"\\server\share or \\server\share\folder";
        path.AccessibleName = "Target network path";
        layout.Controls.Add(path, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = entry.Kind == DriveKind.Network ? "OK replaces the current network mapping." : "This drive letter is available.",
            Dock = DockStyle.Fill, ForeColor = Color.DimGray
        }, 0, 3);

        var login = new GroupBox { Text = "Type of login", Dock = DockStyle.Fill };
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 0, 0) };
        anonymous.Name = "AnonymousRadioButton";
        anonymous.Text = "Anonymous";
        anonymous.AutoSize = true;
        credentials.Name = "CredentialsRadioButton";
        credentials.Text = "Credentials";
        credentials.AutoSize = true;
        credentials.TabIndex = 0;
        anonymous.TabIndex = 1;
        modes.Controls.Add(credentials);
        modes.Controls.Add(anonymous);
        login.Controls.Add(modes);
        layout.Controls.Add(login, 0, 4);
        username.Name = "UsernameTextBox";
        username.PlaceholderText = @"DOMAIN\user or user@domain";
        username.AccessibleName = "Username";
        password.Name = "PasswordTextBox";
        password.UseSystemPasswordChar = true;
        password.AccessibleName = "Password";
        layout.Controls.Add(CreateInputRow("Username", username), 0, 5);
        layout.Controls.Add(CreateInputRow("Password", password), 0, 6);
        openAfterMapping.Name = "OpenAfterMappingCheckBox";
        openAfterMapping.Text = "Open directory after mapping";
        openAfterMapping.Dock = DockStyle.Fill;
        layout.Controls.Add(openAfterMapping, 0, 7);

        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        feedback.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        status.Name = "OperationStatusLabel";
        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = true;
        status.AccessibleName = "Connection status";
        status.TextAlign = ContentAlignment.MiddleLeft;
        readMore.Name = "CredentialsReadMoreLink";
        readMore.Text = "Read more...";
        readMore.AutoSize = true;
        readMore.Visible = false;
        readMore.Margin = new Padding(3, 0, 3, 4);
        readMore.LinkColor = Color.FromArgb(24, 88, 166);
        readMore.AccessibleName = "Read more about anonymous access and credentials";
        progress.Dock = DockStyle.Fill;
        progress.Style = ProgressBarStyle.Marquee;
        progress.Visible = false;
        feedback.Controls.Add(status, 0, 0);
        feedback.Controls.Add(readMore, 0, 1);
        feedback.Controls.Add(progress, 0, 2);
        layout.Controls.Add(feedback, 0, 8);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ConfigureButton(test, "TestConnectionButton", "Test connection", 155);
        ConfigureButton(explorer, "OpenExplorerButton", "Open in Explorer", 170);
        ConfigureButton(cancel, "CancelButton", "Cancel", 110);
        ConfigureButton(ok, "OkButton", "OK", 110);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([test, explorer, cancel, ok]);
        layout.Controls.Add(buttons, 0, 9);
        layout.Controls.Add(new Label
        {
            Text = "Open in Explorer opens the entered path directly; Explorer handles access and login.\nAnonymous access depends on the server and Windows guest-access policy.",
            Dock = DockStyle.Fill, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9)
        }, 0, 10);
        Controls.Add(layout);
    }

    /// <summary>Creates a labeled credential row with the input filling the remaining width.</summary>
    private static TableLayoutPanel CreateInputRow(string label, TextBox input)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        input.Dock = DockStyle.Fill;
        row.Controls.Add(input, 1, 0);
        return row;
    }

    /// <summary>Applies consistent native button sizing and stable names for accessibility/tests.</summary>
    private static void ConfigureButton(Button button, string name, string text, int width)
    {
        button.Name = name;
        button.Text = text;
        button.Size = new Size(width, 34);
        button.Margin = new Padding(0, 4, 8, 4);
    }

    /// <summary>Enables credential fields according to the selected login/session mode and busy state.</summary>
    private void UpdateCredentialControls()
    {
        username.Enabled = credentials.Checked && !busy;
        password.Enabled = credentials.Checked && !busy && !useExistingLogin;
    }

    /// <summary>Builds validated input at the moment an action starts; stale test results are not reused.</summary>
    private ConnectionOptions ReadOptions() => useExistingLogin
        ? ConnectionOptions.ForExistingLogin(path.Text, username.Text)
        : new(path.Text, credentials.Checked, username.Text, password.Text);

    /// <summary>Captures action-specific input on the UI thread, then executes the action on a worker.</summary>
    /// <typeparam name="T">Validated connection options for testing, or raw path text for browsing.</typeparam>
    /// <param name="message">Progress text displayed while the action is running.</param>
    /// <param name="readInput">Reads current controls; browsing never reads or validates login fields.</param>
    /// <param name="operation">Runs with the captured input and returns a success message.</param>
    /// <param name="recoverCredentialConflict">True for Test to offer the same mapping recovery as OK; false for direct browsing.</param>
    private async Task RunOperationAsync<T>(string message, Func<T> readInput, Func<T, string> operation,
        bool recoverCredentialConflict = false)
    {
        if (busy)
            return;
        try
        {
            T input = readInput();
            SetBusy(true, message);
            string result = await Task.Run(() => operation(input));
            status.ForeColor = Color.FromArgb(26, 112, 72);
            status.Text = result;
        }
        catch (Exception error)
        {
            if (recoverCredentialConflict && CredentialConflict.IsConflict(error))
            {
                SetBusy(false);
                await ApplyMappingAsync(error);
            }
            else
                ShowError(error);
        }
        finally
        {
            if (!IsDisposed)
                SetBusy(false);
        }
    }

    /// <summary>Commits the mapping, optionally opens its letter, and closes only after successful mapping.</summary>
    private async Task ApplyMappingAsync(Exception? initialConflict = null)
    {
        if (busy)
            return;
        bool mapped = false;
        bool canceled = false;
        try
        {
            ConnectionOptions provided = ReadOptions();
            ConnectionOptions options = provided;
            DriveEntry expected = entry;
            bool openDirectory = openAfterMapping.Checked;
            Exception? conflict = initialConflict;
            while (!mapped && !canceled)
            {
                if (conflict is null)
                {
                    SetBusy(true, "Checking the target and applying the mapping…");
                    try
                    {
                        await Task.Run(() => service.Apply(expected, options));
                        mapped = true;
                        continue;
                    }
                    catch (Exception error) when (CredentialConflict.IsConflict(error))
                    {
                        // Suppress the ordinary message box for 1219, including rollback wrappers.
                        conflict = error;
                    }
                }

                SetBusy(true, "Reading the existing Windows login and server connections…");
                ServerConnectionReport report = await Task.Run(() => service.GetServerConnections(options.Path));
                SetBusy(false);
                SetErrorFeedback(conflict);
                using CredentialConflictDialog recovery = createConflictDialog(conflict.Message, report);
                recovery.ShowDialog(this);
                if (recovery.Resolution == CredentialConflictResolution.Cancel)
                {
                    canceled = true;
                    continue;
                }

                SetBusy(true, "Preparing the mapping retry…");
                // A failed replacement can leave the old letter unmapped if recovery was impossible.
                // Never overwrite a different mapping that arrived while the choice window was open.
                expected = await Task.Run(() => service.GetRetryDrive(entry));
                if (recovery.Resolution == CredentialConflictResolution.UseExistingLogin)
                {
                    options = ConnectionOptions.ForExistingLogin(provided.Path, report.ExistingLogin
                        ?? throw new InvalidOperationException("Windows did not report an unambiguous existing login."));
                }
                else
                {
                    SetBusy(true, "Disconnecting the confirmed server connections…");
                    await Task.Run(() => service.DisconnectServer(report));
                    expected = await Task.Run(() => service.GetRetryDrive(entry));
                    options = provided;
                }
                ReflectConnectionOptions(options);
                conflict = null;
            }
            if (mapped && openDirectory)
            {
                try
                {
                    await Task.Run(() => service.OpenDrive(entry.Letter));
                }
                catch (Exception error)
                {
                    // A launch error must not be presented as a failed mapping or trigger another remap.
                    MessageBox.Show(this, $"{entry.DeviceName} was mapped successfully, but Explorer could not open it.\n\n{error.Message}",
                        "Drive mapped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        finally
        {
            SetBusy(false);
        }
        if (mapped)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (canceled)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    /// <summary>Reflects the chosen login in the editor, clearing unavailable passwords for existing-session retries.</summary>
    private void ReflectConnectionOptions(ConnectionOptions options)
    {
        useExistingLogin = options.UseExistingLogin;
        credentials.Checked = options.UseCredentials;
        anonymous.Checked = !options.UseCredentials;
        username.Text = options.Username;
        password.Text = options.Password;
        UpdateCredentialControls();
    }

    /// <summary>Returns manual edits to ordinary credential input rather than retaining an obsolete reuse-session mode.</summary>
    private void ConnectionInputChanged()
    {
        if (busy)
            return;
        useExistingLogin = false;
        UpdateCredentialControls();
        ClearStatus();
    }

    /// <summary>Shows the complete actionable error and leaves editable input available for correction.</summary>
    private void ShowError(Exception error)
    {
        SetErrorFeedback(error);
        MessageBox.Show(this, error.Message, "Network connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Displays an error with contextual help for native access denial, including wrapped errors.</summary>
    /// <param name="error">Original test/mapping failure; the native code is used instead of localized message text.</param>
    internal void SetErrorFeedback(Exception error)
    {
        status.ForeColor = Color.Firebrick;
        status.Text = error.Message;
        readMore.Visible = IsAccessDenied(error);
    }

    /// <summary>Opens the explanatory window modally, owned by this editor and without changing its input.</summary>
    internal void ShowCredentialsHelp()
    {
        if (busy)
            return;
        using var help = new CredentialsHelpDialog();
        help.ShowDialog(this);
    }

    /// <summary>Recognizes Windows error 5 and filesystem access denial through mapping-error wrappers.</summary>
    private static bool IsAccessDenied(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is Win32Exception { NativeErrorCode: 5 } or UnauthorizedAccessException)
                return true;
        return false;
    }

    /// <summary>Clears a previous result whenever its input changes, preventing misleading success text.</summary>
    private void ClearStatus()
    {
        if (!busy)
        {
            status.Text = string.Empty;
            readMore.Visible = false;
        }
    }

    /// <summary>Disables edits and closure during a noncancelable native call; always restores login state.</summary>
    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        readMore.Enabled = !value;
        foreach (Control control in new Control[] { path, anonymous, credentials, openAfterMapping, test, explorer, cancel, ok })
            control.Enabled = !value;
        UpdateCredentialControls();
        progress.Visible = value;
        UseWaitCursor = value;
        if (message is not null)
        {
            readMore.Visible = false;
            status.ForeColor = Color.FromArgb(80, 90, 105);
            status.Text = message;
        }
    }
}
