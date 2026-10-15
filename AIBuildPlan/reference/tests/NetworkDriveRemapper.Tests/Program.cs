using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper.Tests;

/// <summary>Dependency-free regression runner with policy tests, native read-only smoke checks, and UI QA.</summary>
internal static class Program
{
    /// <summary>Runs every check on an STA thread; exits nonzero if any assertion fails.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var checks = new (string Name, Action Run)[]
        {
            ("UNC normalization and share root", PathNormalization),
            ("Malformed paths are rejected", InvalidPaths),
            ("Anonymous and explicit credential handling", CredentialHandling),
            ("All 26 letters and available-only filtering", InventoryAndFilter),
            ("Free letter maps with supplied credentials", MapFreeLetter),
            ("Existing mapping is replaced in the correct order", ReplaceMapping),
            ("Mapping removal targets one unchanged drive and preserves reserved, changed, or in-use letters", RemoveMapping),
            ("Only native error 2401 offers force removal with the original error and work-loss warning", ForceRemovalDetection),
            ("Removal confirmation handles Yes, No, retry failure, unrelated errors, and ownership changes", ForceRemovalWorkflow),
            ("Same target still applies the newly supplied login", ReplaceSameTarget),
            ("Invalid target preserves the old mapping", FailedPreflight),
            ("Open-file failure prevents replacement", FailedUnmap),
            ("Failed replacement restores the old mapping", SuccessfulRollback),
            ("Failed rollback reports both failures", FailedRollback),
            ("Failed free mapping does not try restoration", FailedFreeMapping),
            ("Stale dialog never changes the letter", StaleSnapshot),
            ("Ownership is rechecked after network preflight", ConcurrentChange),
            ("Local and unknown network letters cannot be edited", ReservedLetters),
            ("Windows inventory smoke check is read-only", NativeInventory),
            ("Dialog defaults, login controls, test, browse, cancel", DialogControls),
            ("OK maps and honors both Explorer checkbox values", DialogMapping),
            ("Access-denied help, exact explanation, modal ownership, and documentation links", () => CredentialsHelpAndRender(args)),
            ("1219 detection and password-free existing-session options", ConflictOptions),
            ("Confirmed server drop leaves other servers intact", ServerScopedDisconnect),
            ("Incomplete, changed, or cross-server lists are never dropped", DisconnectGuards),
            ("Partial disconnect failure stops before mapping", DisconnectFailure),
            ("Retry snapshots protect newly assigned drive letters", RetryOwnership),
            ("Native connection listing works without changing mappings", NativeServerConnections),
            ("Conflict review preserves the error and confirms destructive choices", () => ConflictReviewControls(args)),
            ("Mapping and testing recover 1219 through all three choices", ConflictRecoveryWorkflow),
            ("Row icon conditions, click actions, refresh, and exact dynamic scaling", () => MainRowActionsAndScaling(args)),
            ("Main grid keeps rows, selection, scrolling, and appearance stable across actions", MainRefreshStability),
            ("Main grid filtering and window render", () => MainGridAndRender(args))
        };
        int failures = 0;
        // Keep a real root message loop alive while tests open nested modal dialogs. DoEvents alone
        // can let WinForms tear down/reinstall its synchronization context between async continuations.
        using var host = new Form
        {
            Name = "RegressionMessageLoopHost", ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Size = new Size(1, 1)
        };
        host.Shown += (_, _) => host.BeginInvoke((Action)(() =>
        {
            foreach ((string name, Action run) in checks)
            {
                try
                {
                    run();
                    Console.WriteLine($"PASS {name}");
                }
                catch (Exception error)
                {
                    failures++;
                    Console.WriteLine($"FAIL {name}: {error}");
                }
            }
            host.Close();
        }));
        Application.Run(host);
        Console.WriteLine($"\n{checks.Length - failures}/{checks.Length} checks passed.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Checks common share/folder input, spaces, trailing separators, and normalized share extraction.</summary>
    private static void PathNormalization()
    {
        var options = new ConnectionOptions(@"  \\server\team share\folder\  ", false);
        Equal(@"\\server\team share\folder", options.Path);
        Equal(@"\\server\team share", options.ShareRoot);
        Equal(@"\\server\share$", new ConnectionOptions(@"\\server\share$", false).Path);
    }

    /// <summary>Rejects local paths, device namespaces, incomplete UNC paths, traversal, and invalid characters.</summary>
    private static void InvalidPaths()
    {
        foreach (string path in new[] { "", "C:\\folder", @"\server\share", @"\\server", @"\\?\UNC\server\share",
            @"\\.\device\share", @"\\server\\share", @"\\server\share\..\folder", @"\\server\share\file*",
            @"\\server\share\folder.", "\\\\server\\share\\bad\nname" })
            Throws<ArgumentException>(() => new ConnectionOptions(path, false));
    }

    /// <summary>Verifies username validation, password whitespace, empty passwords, and safe diagnostic strings.</summary>
    private static void CredentialHandling()
    {
        var anonymous = new ConnectionOptions(@"\\server\share", false, "ignored", "ignored-password");
        Equal(string.Empty, anonymous.Username);
        Equal(string.Empty, anonymous.Password);
        var explicitLogin = new ConnectionOptions(@"\\server\share", true, " DOMAIN\\alice ", " secret ");
        Equal(@"DOMAIN\alice", explicitLogin.Username);
        Equal(" secret ", explicitLogin.Password);
        Equal(explicitLogin.Path, explicitLogin.ToString());
        _ = new ConnectionOptions(@"\\server\share", true, "alice", "");
        Throws<ArgumentException>(() => new ConnectionOptions(@"\\server\share", true, " ", "pw"));
        Throws<ArgumentException>(() => new ConnectionOptions(@"\\server\share", true, "alice", "pw\0suffix"));
    }

    /// <summary>Confirms filtering excludes both local disks and network mappings, including disconnected entries.</summary>
    private static void InventoryAndFilter()
    {
        var fake = new FakeNetworkDriveSystem();
        fake.Set(new('C', DriveKind.Occupied, "Taken - Hard Drive"));
        fake.Set(OldMapping());
        var service = new DriveMappingService(fake);
        Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ", new string(service.GetDrives().Select(entry => entry.Letter).ToArray()));
        Equal(24, service.GetDrives(true).Count);
        Check(service.GetDrives(true).All(entry => entry.Kind == DriveKind.Free), "Only unused letters must remain.");
    }

    /// <summary>Checks preflight order, absence of unmapping, and exact forwarding of credentials/persistence.</summary>
    private static void MapFreeLetter()
    {
        var fake = new FakeNetworkDriveSystem();
        var options = NewOptions();
        new DriveMappingService(fake).Apply(fake.Drives[25], options);
        Equal("read,test,read,map", string.Join(',', fake.Calls));
        Check(ReferenceEquals(options, fake.LastOptions), "The requested credentials must be forwarded intact.");
        Check(fake.LastPersistent, "New mappings should persist across sign-in.");
    }

    /// <summary>Checks that a remembered mapping is disconnected only after a successful target test.</summary>
    private static void ReplaceMapping()
    {
        var fake = ExistingMappingSystem();
        new DriveMappingService(fake).Apply(OldMapping(), NewOptions());
        Equal("read,test,read,unmap,map", string.Join(',', fake.Calls));
        Equal(NewOptions().Path, fake.Drives[25].NetworkPath);
    }

    /// <summary>Verifies removal scope, stale-snapshot protection, reserved/free rejection, and non-destructive unmap failures.</summary>
    private static void RemoveMapping()
    {
        var fake = ExistingMappingSystem();
        DriveEntry other = new('B', DriveKind.Network, @"\\other-server\share", @"\\other-server\share", true);
        fake.Set(other);
        new DriveMappingService(fake).Remove(OldMapping());
        Equal("read,unmap", string.Join(',', fake.Calls));
        Equal(('Z', false), fake.UnmapAttempts.Single());
        Equal(DriveKind.Free, fake.Drives[25].Kind);
        Equal(other, fake.Drives[1]);
        Check(!fake.Drives[25].IsPersistent, "Removing a mapping must also remove its remembered profile.");

        foreach (DriveEntry invalid in new DriveEntry[]
        {
            new('A', DriveKind.Free, "Available"), new('C', DriveKind.Occupied, "Taken - Hard Drive"),
            new('Z', DriveKind.Network, "Unknown target"), new('@', DriveKind.Network, "Invalid", @"\\server\share")
        })
        {
            fake = ExistingMappingSystem();
            Throws<InvalidOperationException>(() => new DriveMappingService(fake).Remove(invalid));
            Check(!fake.Calls.Contains("unmap"), "Free, reserved, unknown, and invalid letters must never be removed.");
        }
        foreach (DriveEntry changed in new DriveEntry[]
        {
            new('Z', DriveKind.Free, "Available"), new('Z', DriveKind.Occupied, "Taken - Hard Drive"),
            new('Z', DriveKind.Network, @"\\different\share", @"\\different\share", true),
            OldMapping() with { IsPersistent = false }
        })
        {
            fake = ExistingMappingSystem();
            fake.Set(changed);
            Throws<InvalidOperationException>(() => new DriveMappingService(fake).Remove(OldMapping()));
            Equal("read", string.Join(',', fake.Calls));
            Equal(changed, fake.Drives[25]);
        }
        fake = ExistingMappingSystem();
        fake.UnmapError = new IOException("Open files");
        Throws<IOException>(() => new DriveMappingService(fake).Remove(OldMapping()));
        Equal("read,unmap", string.Join(',', fake.Calls));
        Equal(OldMapping(), fake.Drives[25]);
    }

    /// <summary>Prevents same-path optimizations from ignoring a newly entered login.</summary>
    private static void ReplaceSameTarget()
    {
        var fake = ExistingMappingSystem();
        var options = new ConnectionOptions(OldMapping().NetworkPath!, true, "different-user", "different-password");
        new DriveMappingService(fake).Apply(OldMapping(), options);
        Check(fake.Calls.Contains("unmap") && fake.Calls.Contains("map"), "A login change needs a new mapping.");
        Equal("different-user", fake.LastOptions!.Username);
    }

    /// <summary>Ensures a bad target or password cannot remove an existing mapping.</summary>
    private static void FailedPreflight()
    {
        var fake = ExistingMappingSystem();
        fake.TestError = new IOException("Target unreachable");
        Throws<IOException>(() => new DriveMappingService(fake).Apply(OldMapping(), NewOptions()));
        Equal("read,test", string.Join(',', fake.Calls));
        Equal(OldMapping(), fake.Drives[25]);
    }

    /// <summary>Ensures an in-use old drive is neither force-disconnected nor mapped over.</summary>
    private static void FailedUnmap()
    {
        var fake = ExistingMappingSystem();
        fake.UnmapError = new IOException("Open files");
        Throws<IOException>(() => new DriveMappingService(fake).Apply(OldMapping(), NewOptions()));
        Check(!fake.Calls.Contains("map") && !fake.Calls.Contains("restore"), "An unmap failure must stop the operation.");
        Equal(('Z', false), fake.UnmapAttempts.Single());
        Equal(OldMapping(), fake.Drives[25]);
    }

    /// <summary>Checks exact native-code matching and the full original error, drive identity, and explicit force warning text.</summary>
    private static void ForceRemovalDetection()
    {
        var error = new Win32Exception(2401, "Disconnect Z: failed (Windows error 2401): Open files or requests pending.");
        Check(MainForm.IsOpenFilesError(error), "Native error 2401 must offer confirmation.");
        Check(MainForm.IsOpenFilesError(new IOException("Wrapper", error)), "Wrapped native error 2401 must offer confirmation.");
        foreach (Exception unrelated in new Exception[]
        {
            new Win32Exception(5, "2401 in text"), new Win32Exception(2404, "Device in use"),
            new IOException("Windows error 2401"), new InvalidOperationException("Other")
        })
            Check(!MainForm.IsOpenFilesError(unrelated), "Other native codes and message-only matches must not offer force.");
        string prompt = MainForm.BuildForceRemovalPrompt(OldMapping(), error);
        Check(prompt.StartsWith(error.Message + "\n\n", StringComparison.Ordinal), "The prompt must preserve the entire original error.");
        Check(prompt.Contains("Z:") && prompt.Contains(OldMapping().NetworkPath!), "Identify the exact mapping being removed.");
        Check(prompt.Contains("Unsaved changes") && prompt.Contains("pending writes") && prompt.Contains("file transfers"),
            "Explain the consequences before authorizing force.");
        Check(prompt.EndsWith("Do you want to force disconnect this drive?", StringComparison.Ordinal), "Ask the explicit force question.");
    }

    /// <summary>Exercises the actual REMOVE event using fake Windows failures and injected message callbacks, without disconnecting real drives.</summary>
    private static void ForceRemovalWorkflow()
    {
        var openFiles = new Win32Exception(2401, "Disconnect Z: failed (Windows error 2401): Open files or requests pending.");
        foreach (var scenario in new (Exception Initial, bool AnswerYes, bool ChangeMapping, Exception? RetryError)[]
        {
            (openFiles, false, false, null),
            (openFiles, true, false, null),
            (new IOException("Wrapped disconnect failure", openFiles), true, false, null),
            (openFiles, true, false, new Win32Exception(5, "Forced retry denied")),
            (openFiles, true, false, openFiles),
            (openFiles, true, true, null),
            (new Win32Exception(5, "Access denied"), true, false, null),
            (new Win32Exception(2404, "Device in use"), true, false, null),
            (new IOException("Windows error 2401 appears only in text"), true, false, null)
        })
        {
            var fake = ExistingMappingSystem();
            DriveEntry other = new('B', DriveKind.Network, @"\\unrelated\share", @"\\unrelated\share", true);
            DriveEntry changed = new('Z', DriveKind.Network, @"\\changed\share", @"\\changed\share", true);
            fake.Set(other);
            fake.UnmapFailures.Enqueue(scenario.Initial);
            if (scenario.RetryError is not null)
                fake.UnmapFailures.Enqueue(scenario.RetryError);
            int prompts = 0;
            var errors = new List<string>();
            MainForm? ownerForm = null;
            using var form = new MainForm(new DriveMappingService(fake), (owner, message) =>
            {
                Check(ReferenceEquals(ownerForm, owner), "Force confirmation must belong to the main window.");
                prompts++;
                Check(message.StartsWith(scenario.Initial.Message + "\n\n", StringComparison.Ordinal), "Keep the original error in the warning.");
                if (scenario.ChangeMapping)
                    fake.Set(changed);
                return scenario.AnswerYes;
            }, (owner, message) =>
            {
                Check(ReferenceEquals(ownerForm, owner), "Removal errors must belong to the main window.");
                errors.Add(message);
            });
            ownerForm = form;
            ShowOffscreen(form);
            DataGridView grid = Find<DataGridView>(form, "DrivesGrid");
            Button refresh = Find<Button>(form, "RefreshButton");
            PumpUntil(() => grid.Rows.Count == 26 && refresh.Enabled);
            DataGridViewRow originalRow = grid.Rows[25];
            int rowChanges = 0, enabledChanges = 0;
            grid.RowsAdded += (_, _) => rowChanges++;
            grid.RowsRemoved += (_, _) => rowChanges++;
            grid.EnabledChanged += (_, _) => enabledChanges++;
            grid.CurrentCell = grid.Rows[0].Cells["EditAction"];
            ActivateGridAction(grid, 25, "RemoveAction");
            PumpUntil(() => refresh.Enabled);
            bool eligible = MainForm.IsOpenFilesError(scenario.Initial);
            bool retry = eligible && scenario.AnswerYes && !scenario.ChangeMapping;
            Equal(eligible ? 1 : 0, prompts);
            Equal(retry ? 2 : 1, fake.UnmapAttempts.Count);
            Equal(('Z', false), fake.UnmapAttempts[0]);
            if (retry)
                Equal(('Z', true), fake.UnmapAttempts[1]);
            bool success = retry && scenario.RetryError is null;
            Equal(!eligible || (scenario.AnswerYes && !success) ? 1 : 0, errors.Count);
            Equal(success ? DriveKind.Free : DriveKind.Network, fake.Drives[25].Kind);
            Equal(scenario.ChangeMapping ? changed : success ? fake.Drives[25] : OldMapping(), fake.Drives[25]);
            Equal(other, fake.Drives[1]);
            Equal(fake.Drives[25], grid.Rows[25].Tag);
            Check(ReferenceEquals(originalRow, grid.Rows[25]), "Confirmation/retry must retain the row without rebuilding the grid.");
            Equal(0, rowChanges);
            Equal(0, enabledChanges);
            if (scenario.RetryError is not null)
                Check(errors.Single().Contains(scenario.RetryError.Message), "Report a forced retry failure once without prompting again.");
            if (!eligible)
                Equal(scenario.Initial.Message, errors.Single());
            Check(form.Visible && refresh.Enabled, "Yes/No/error completion must return to the usable main window.");
            form.Close();
        }
    }

    /// <summary>Verifies successful rollback preserves the old path and its persistence metadata.</summary>
    private static void SuccessfulRollback()
    {
        var fake = ExistingMappingSystem();
        fake.MapError = new IOException("New login conflict");
        var error = Throws<InvalidOperationException>(() => new DriveMappingService(fake).Apply(OldMapping(), NewOptions()));
        Check(error.Message.Contains("was restored"), "Report successful recovery.");
        Equal(OldMapping(), fake.LastRestored);
        Equal(OldMapping(), fake.Drives[25]);
    }

    /// <summary>Verifies failed recovery includes the old target and both error reasons.</summary>
    private static void FailedRollback()
    {
        var fake = ExistingMappingSystem();
        fake.MapError = new IOException("New login conflict");
        fake.RestoreError = new IOException("Old login unavailable");
        var error = Throws<InvalidOperationException>(() => new DriveMappingService(fake).Apply(OldMapping(), NewOptions()));
        Check(error.Message.Contains("New login conflict") && error.Message.Contains("Old login unavailable")
            && error.Message.Contains(OldMapping().NetworkPath!), "Both failures and the old target must be visible.");
        Equal(DriveKind.Free, fake.Drives[25].Kind);
    }

    /// <summary>Ensures a free-letter failure does not attempt to invent an old mapping.</summary>
    private static void FailedFreeMapping()
    {
        var fake = new FakeNetworkDriveSystem { MapError = new IOException("Unavailable") };
        Throws<IOException>(() => new DriveMappingService(fake).Apply(fake.Drives[25], NewOptions()));
        Check(!fake.Calls.Contains("restore"), "A previously unused letter has nothing to restore.");
    }

    /// <summary>Ensures stale dialogs fail before even testing the proposed server.</summary>
    private static void StaleSnapshot()
    {
        var fake = ExistingMappingSystem();
        fake.Set(new('Z', DriveKind.Network, @"\\other\share", @"\\other\share", true));
        Throws<InvalidOperationException>(() => new DriveMappingService(fake).Apply(OldMapping(), NewOptions()));
        Equal("read", string.Join(',', fake.Calls));
    }

    /// <summary>Simulates a physical volume arriving during preflight and verifies no mutation follows.</summary>
    private static void ConcurrentChange()
    {
        var fake = new FakeNetworkDriveSystem();
        DriveEntry expected = fake.Drives[25];
        fake.DuringTest = () => fake.Set(new('Z', DriveKind.Occupied, "Taken - Hard Drive"));
        Throws<InvalidOperationException>(() => new DriveMappingService(fake).Apply(expected, NewOptions()));
        Equal("read,test,read", string.Join(',', fake.Calls));
    }

    /// <summary>Checks reserved sources and unknown-target network entries cannot be mapped.</summary>
    private static void ReservedLetters()
    {
        var fake = new FakeNetworkDriveSystem();
        var service = new DriveMappingService(fake);
        Throws<InvalidOperationException>(() => service.Apply(new('C', DriveKind.Occupied, "Taken"), NewOptions()));
        Throws<InvalidOperationException>(() => service.Apply(new('Z', DriveKind.Network, "Unknown"), NewOptions()));
        Check(fake.Calls.Count == 0, "Invalid source entries must never reach Windows.");
    }

    /// <summary>Runs production Win32 discovery without mapping, browsing, or authenticating to a share.</summary>
    private static void NativeInventory()
    {
        IReadOnlyList<DriveEntry> entries = new WindowsNetworkDriveSystem().GetDrives();
        Equal(26, entries.Count);
        Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ", new string(entries.Select(entry => entry.Letter).ToArray()));
        foreach (string root in Directory.GetLogicalDrives())
            Check(entries.Single(entry => entry.Letter == root[0]).Kind != DriveKind.Free,
                $"The assigned letter {root} cannot be reported as free.");
        Console.WriteLine("     " + string.Join("; ", entries.Where(entry => entry.Kind != DriveKind.Free)
            .Select(entry => $"{entry.DeviceName} {entry.Description}")));
    }

    /// <summary>Exercises real WinForms controls with a fake backend, including explicit Cancel behavior.</summary>
    private static void DialogControls()
    {
        var fake = new FakeNetworkDriveSystem();
        using var dialog = new MappingDialog(new DriveMappingService(fake), fake.Drives[25]);
        ShowOffscreen(dialog);
        RadioButton credentials = Find<RadioButton>(dialog, "CredentialsRadioButton");
        RadioButton anonymous = Find<RadioButton>(dialog, "AnonymousRadioButton");
        Check(credentials.Checked && !anonymous.Checked, "Free letters must default to Credentials.");
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            Equal(identity.Name, Find<TextBox>(dialog, "UsernameTextBox").Text);
        Equal(string.Empty, Find<TextBox>(dialog, "PasswordTextBox").Text);
        Check(credentials.Left < anonymous.Left && credentials.TabIndex < anonymous.TabIndex,
            "Credentials must appear before Anonymous visually and in keyboard order.");
        Check(Find<CheckBox>(dialog, "OpenAfterMappingCheckBox").Checked, "Opening after mapping defaults to true.");
        Check(Find<TextBox>(dialog, "UsernameTextBox").Enabled && Find<TextBox>(dialog, "PasswordTextBox").Enabled,
            "Free-letter credential fields must start enabled.");
        Check(Find<TextBox>(dialog, "PasswordTextBox").UseSystemPasswordChar, "Password must be masked.");
        string initialAccount = Find<TextBox>(dialog, "UsernameTextBox").Text;
        anonymous.Checked = true;
        Check(!Find<TextBox>(dialog, "UsernameTextBox").Enabled && !Find<TextBox>(dialog, "PasswordTextBox").Enabled,
            "Selecting Anonymous disables both credential fields.");
        credentials.Checked = true;
        Equal(initialAccount, Find<TextBox>(dialog, "UsernameTextBox").Text);
        Check(Find<TextBox>(dialog, "UsernameTextBox").Enabled && Find<TextBox>(dialog, "PasswordTextBox").Enabled,
            "Selecting Credentials enables both fields.");
        Find<TextBox>(dialog, "TargetPathTextBox").Text = @"\\server\share";
        Find<TextBox>(dialog, "UsernameTextBox").Text = "alice";
        Find<TextBox>(dialog, "PasswordTextBox").Text = "pw";
        Find<Button>(dialog, "TestConnectionButton").PerformClick();
        PumpUntil(() => Find<Label>(dialog, "OperationStatusLabel").Text.StartsWith("Connection succeeded"));
        Equal("alice", fake.LastOptions!.Username);
        Check(!fake.Calls.Contains("map") && !fake.Calls.Contains("unmap"), "Testing does not modify letters.");
        // Browsing must use the CURRENT text, including local paths, even with incomplete login
        // input and a backend that would fail any connection test. No options validation is allowed.
        fake.TestError = new IOException("Network is unavailable");
        Find<TextBox>(dialog, "UsernameTextBox").Clear();
        string enteredPath = @"C:\typed folder\another folder";
        Find<TextBox>(dialog, "TargetPathTextBox").Text = enteredPath;
        Equal(string.Empty, Find<Label>(dialog, "OperationStatusLabel").Text);
        Find<Button>(dialog, "OpenExplorerButton").PerformClick();
        PumpUntil(() => Find<Label>(dialog, "OperationStatusLabel").Text.StartsWith("The location was sent"));
        Check(fake.Calls.Contains("browse"), "Explorer must open even when network tests would fail.");
        Equal(enteredPath, fake.LastOpenedPath);
        Equal(1, fake.Calls.Count(call => call == "test"));
        // An unreachable UNC target is passed on unchanged too; Explorer owns connection feedback.
        string networkPath = @"\\unreachable-server\share\entered folder";
        Find<TextBox>(dialog, "TargetPathTextBox").Text = networkPath;
        Find<Button>(dialog, "OpenExplorerButton").PerformClick();
        PumpUntil(() => Find<Label>(dialog, "OperationStatusLabel").Text.StartsWith("The location was sent"));
        Equal(networkPath, fake.LastOpenedPath);
        Equal(1, fake.Calls.Count(call => call == "test"));
        TextBox passwordControl = Find<TextBox>(dialog, "PasswordTextBox");
        Find<Button>(dialog, "CancelButton").PerformClick();
        PumpUntil(() => !dialog.Visible);
        Equal(DialogResult.Cancel, dialog.DialogResult);
        Check(!fake.Calls.Contains("map") && !fake.Calls.Contains("unmap"), "Cancel must not apply a mapping.");
        Equal(string.Empty, passwordControl.Text);
        using var mappedDialog = new MappingDialog(new DriveMappingService(fake), OldMapping());
        ShowOffscreen(mappedDialog);
        Check(Find<RadioButton>(mappedDialog, "AnonymousRadioButton").Checked,
            "Existing mappings must retain their previous default login mode.");
        Equal(OldMapping().NetworkPath, Find<TextBox>(mappedDialog, "TargetPathTextBox").Text);
        Equal(string.Empty, Find<TextBox>(mappedDialog, "UsernameTextBox").Text);
        Equal(string.Empty, Find<TextBox>(mappedDialog, "PasswordTextBox").Text);
        Check(Find<RadioButton>(mappedDialog, "CredentialsRadioButton").Left
            < Find<RadioButton>(mappedDialog, "AnonymousRadioButton").Left, "Swapped login order applies to existing mappings too.");
        mappedDialog.Close();
    }

    /// <summary>Uses the actual OK button to verify mapping and the checked/unchecked Explorer preference.</summary>
    private static void DialogMapping()
    {
        foreach (bool open in new[] { true, false })
        {
            var fake = new FakeNetworkDriveSystem();
            using var dialog = new MappingDialog(new DriveMappingService(fake), fake.Drives[25]);
            ShowOffscreen(dialog);
            Find<TextBox>(dialog, "TargetPathTextBox").Text = @"\\server\share";
            Find<CheckBox>(dialog, "OpenAfterMappingCheckBox").Checked = open;
            Find<Button>(dialog, "OkButton").PerformClick();
            PumpUntil(() => !dialog.Visible);
            Equal(DialogResult.OK, dialog.DialogResult);
            Equal(DriveKind.Network, fake.Drives[25].Kind);
            Check(fake.LastOptions!.UseCredentials && !fake.LastOptions.UseExistingLogin,
                "New-drive defaults must submit ordinary named credentials, not silently reuse a Windows session.");
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                Equal(identity.Name, fake.LastOptions.Username);
            Equal(string.Empty, fake.LastOptions.Password);
            Equal(open, fake.Calls.Contains("explorer:Z"));
        }
    }

    /// <summary>Verifies typed 1219 detection, wrapped errors, and reuse options that never carry a recovered password.</summary>
    private static void ConflictOptions()
    {
        Check(CredentialConflict.IsConflict(ConflictError()), "Native 1219 must be recognized.");
        Check(CredentialConflict.IsConflict(new IOException("Wrapper", ConflictError())), "Wrapped 1219 must be recognized.");
        Check(CredentialConflict.IsConflict(new IOException("Filesystem conflict", unchecked((int)0x800704C3))),
            "Filesystem HRESULTs carrying Windows error 1219 should use the same recovery.");
        Check(!CredentialConflict.IsConflict(new Win32Exception(5, "1219 mentioned only in text")), "Detection must use native codes.");
        ConnectionOptions existing = ConnectionOptions.ForExistingLogin(@"\\new-server\share", @"DOMAIN\Existing");
        Check(existing.UseExistingLogin && existing.UseCredentials, "The retry should explicitly select the existing named session.");
        Equal(string.Empty, existing.Password);
        Throws<ArgumentException>(() => ConnectionOptions.ForExistingLogin(@"\\new-server\share", ""));
        var report = new ServerConnectionReport(existing.Path,
            [new(null, @"\\new-server\other", @"DOMAIN\Existing", "Connected")], []);
        Equal(@"DOMAIN\Existing", report.ExistingLogin);
        Check(report.ContainsServer(@"\\NEW-SERVER.\share") && !report.ContainsServer(@"\\new-server-extra\share"),
            "Server matching must be exact, case-insensitive, and tolerate a trailing DNS dot.");
        Equal<string?>(null, (report with { Connections = [.. report.Connections, new(null, @"\\new-server\third", "Other", "Connected")] }).ExistingLogin);
    }

    /// <summary>Exercises confirmed server-scoped disconnection and checks that unrelated resources remain intact.</summary>
    private static void ServerScopedDisconnect()
    {
        FakeNetworkDriveSystem fake = ConflictSystem();
        var service = new DriveMappingService(fake);
        ServerConnectionReport report = service.GetServerConnections(NewOptions().Path);
        service.DisconnectServer(report);
        Check(fake.LastDisconnectWasForced, "The confirmed destructive choice permits forced closure.");
        Equal(0, fake.GetServerConnections(NewOptions().Path).Connections.Count);
        Check(fake.Connections.Single().RemotePath == @"\\unrelated-server\share", "Other servers must remain connected.");
        Equal(DriveKind.Free, fake.Drives['L' - 'A'].Kind);
        Check(!fake.Calls.Contains("map"), "Dropping connections alone does not silently create a mapping.");
    }

    /// <summary>Checks that incomplete enumeration, stale accounts, and a forged cross-server scope prevent all drops.</summary>
    private static void DisconnectGuards()
    {
        FakeNetworkDriveSystem fake = ConflictSystem();
        var service = new DriveMappingService(fake);
        ServerConnectionReport reviewed = service.GetServerConnections(NewOptions().Path);
        fake.DiscoveryWarnings.Add("Provider could not be read");
        Throws<InvalidOperationException>(() => service.DisconnectServer(reviewed));
        fake.DiscoveryWarnings.Clear();
        fake.Connections.Add(new(null, @"\\new-server\new-share", "New login", "Connected"));
        Throws<InvalidOperationException>(() => service.DisconnectServer(reviewed));
        Throws<InvalidOperationException>(() => service.DisconnectServer(reviewed with
        {
            Connections = [new(null, @"\\unrelated-server\share", "Other", "Connected")]
        }));
        Check(!fake.Calls.Any(call => call.StartsWith("drop:")), "No unsafe list may reach disconnection.");
    }

    /// <summary>Simulates a later failed disconnection and verifies partial results are reported with no mapping retry.</summary>
    private static void DisconnectFailure()
    {
        FakeNetworkDriveSystem fake = ConflictSystem();
        fake.DisconnectFailures.Enqueue(null);
        fake.DisconnectFailures.Enqueue(new IOException("Resource could not be disconnected"));
        var service = new DriveMappingService(fake);
        var error = Throws<InvalidOperationException>(() => service.DisconnectServer(service.GetServerConnections(NewOptions().Path)));
        Check(error.Message.Contains("1 connection(s) were already disconnected") && error.Message.Contains("Mapping was not retried"),
            "Partial disconnection must be explicit.");
        Check(!fake.Calls.Contains("map"), "Failure stops before mapping with new credentials.");
        Check(fake.Connections.Any(connection => connection.RemotePath == @"\\unrelated-server\share"), "An unrelated server remains untouched.");
    }

    /// <summary>Checks safe retry after failed rollback while rejecting a new device or unrelated mapping at the chosen letter.</summary>
    private static void RetryOwnership()
    {
        var fake = ExistingMappingSystem();
        var service = new DriveMappingService(fake);
        Equal(OldMapping(), service.GetRetryDrive(OldMapping()));
        fake.Set(new('Z', DriveKind.Free, "Available"));
        Equal(DriveKind.Free, service.GetRetryDrive(OldMapping()).Kind);
        fake.Set(new('Z', DriveKind.Network, @"\\different\share", @"\\different\share"));
        Throws<InvalidOperationException>(() => service.GetRetryDrive(OldMapping()));
        fake.Set(new('Z', DriveKind.Occupied, "Taken - Hard Drive"));
        Throws<InvalidOperationException>(() => service.GetRetryDrive(OldMapping()));
    }

    /// <summary>Runs the actual non-elevated connection APIs read-only and verifies complete, server-scoped metadata.</summary>
    private static void NativeServerConnections()
    {
        var system = new WindowsNetworkDriveSystem();
        string path = system.GetDrives().FirstOrDefault(drive => drive.Kind == DriveKind.Network)?.NetworkPath
            ?? @"\\NetworkDriveRemapper-NoSuchServer\share";
        ServerConnectionReport report = system.GetServerConnections(path);
        Check(report.Warnings.Count == 0, string.Join("; ", report.Warnings));
        Check(report.Connections.All(connection => report.ContainsServer(connection.RemotePath)), "Native discovery must remain server scoped.");
        Console.WriteLine($"     Current-logon connections found: {report.Connections.Count}; login reported: {report.ExistingLogin is not null}");
    }

    /// <summary>Verifies the review information, No/Yes behavior, existing-login action, Cancel, and optional layout renders.</summary>
    private static void ConflictReviewControls(string[] args)
    {
        FakeNetworkDriveSystem fake = ConflictSystem();
        ServerConnectionReport report = fake.GetServerConnections(NewOptions().Path);
        int confirmations = 0;
        string warning = string.Empty;
        using var review = new CredentialConflictDialog(ConflictError().Message, report, (_, text) =>
        {
            warning = text;
            return ++confirmations > 1;
        });
        ShowOffscreen(review);
        Equal(ConflictError().Message, Find<TextBox>(review, "CredentialConflictErrorText").Text);
        Check(Find<Label>(review, "ExistingServerLoginLabel").Text.Contains(@"DOMAIN\Existing"), "Show the actual connection account.");
        Equal(2, Find<DataGridView>(review, "ServerConnectionsGrid").Rows.Count);
        Find<Button>(review, "DisconnectServerButton").PerformClick();
        Check(review.Visible && review.Resolution == CredentialConflictResolution.Cancel, "No must return to this same options dialog.");
        Check(warning.Contains("Unsaved changes may be lost") && warning.Contains("Do you really want to proceed?")
            && report.Connections.All(connection => warning.Contains(connection.RemotePath)), "Confirmation must describe risks and every affected connection.");
        Check(!fake.Calls.Any(call => call.StartsWith("drop:")), "The review dialog must not disconnect before controller execution.");
        if (args.Contains("--render"))
        {
            Directory.CreateDirectory(Path.Combine("artifacts", "ui"));
            SaveRender(review, Path.Combine("artifacts", "ui", "credential-conflict.png"));
        }
        review.Size = review.MinimumSize;
        review.PerformLayout();
        Application.DoEvents();
        foreach (string name in new[] { "UseExistingLoginButton", "DisconnectServerButton", "CancelConflictButton" })
        {
            Button action = Find<Button>(review, name);
            Check(action.Right <= action.Parent!.ClientSize.Width, "Every recovery button must fit at the minimum window size.");
        }
        if (args.Contains("--render"))
            SaveRender(review, Path.Combine("artifacts", "ui", "credential-conflict-minimum.png"));
        Find<Button>(review, "DisconnectServerButton").PerformClick();
        Equal(CredentialConflictResolution.DisconnectAndRetry, review.Resolution);
        Check(!review.Visible, "Yes completes the chosen action for the mapping controller.");

        using var existing = new CredentialConflictDialog(ConflictError().Message, report);
        ShowOffscreen(existing);
        Find<Button>(existing, "UseExistingLoginButton").PerformClick();
        Equal(CredentialConflictResolution.UseExistingLogin, existing.Resolution);
        using var cancel = new CredentialConflictDialog(ConflictError().Message, report);
        ShowOffscreen(cancel);
        Find<Button>(cancel, "CancelConflictButton").PerformClick();
        Equal(CredentialConflictResolution.Cancel, cancel.Resolution);
        Check(!cancel.Visible, "Cancel closes the review.");
        using var unknown = new CredentialConflictDialog(ConflictError().Message, report with
        {
            Connections = [new(null, @"\\new-server\share", null, "Connected")], Warnings = ["Incomplete list"]
        });
        ShowOffscreen(unknown);
        Check(!Find<Button>(unknown, "UseExistingLoginButton").Enabled && !Find<Button>(unknown, "DisconnectServerButton").Enabled,
            "Unknown login or incomplete listing must disable actions that cannot be performed reliably.");
        unknown.Close();
    }

    /// <summary>Runs real OK/Test event handlers through each 1219 resolution, with fake network effects and safe confirmation.</summary>
    private static void ConflictRecoveryWorkflow()
    {
        foreach ((CredentialConflictResolution resolution, bool testFirst) in new[]
        {
            (CredentialConflictResolution.UseExistingLogin, false),
            (CredentialConflictResolution.DisconnectAndRetry, false),
            (CredentialConflictResolution.Cancel, false),
            (CredentialConflictResolution.UseExistingLogin, true)
        })
        {
            FakeNetworkDriveSystem fake = ConflictSystem();
            // A remembered old mapping on this server exercises rollback wrappers and rebasing after bulk drop.
            DriveEntry original = new('Z', DriveKind.Network, @"\\new-server\old", @"\\new-server\old", true);
            fake.Set(original);
            fake.Connections.Add(new("Z:", original.NetworkPath!, @"DOMAIN\Existing", "Connected"));
            if (testFirst)
                fake.TestFailures.Enqueue(ConflictError());
            else
                fake.MappingFailures.Enqueue(ConflictError());
            int reviews = 0;
            Exception? eventFailure = null;
            using var mapping = new MappingDialog(new DriveMappingService(fake), original, (message, report) =>
            {
                var review = new CredentialConflictDialog(message, report, (_, _) => true);
                review.Shown += (_, _) => review.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        reviews++;
                        Check(review.Modal && review.Owner is MappingDialog, "The recovery review must be modal and owned by the mapping editor.");
                        string button = resolution switch
                        {
                            CredentialConflictResolution.UseExistingLogin => "UseExistingLoginButton",
                            CredentialConflictResolution.DisconnectAndRetry => "DisconnectServerButton",
                            _ => "CancelConflictButton"
                        };
                        Find<Button>(review, button).PerformClick();
                    }
                    catch (Exception error)
                    {
                        eventFailure = error;
                        review.Close();
                    }
                }));
                return review;
            });
            ShowOffscreen(mapping);
            Find<TextBox>(mapping, "TargetPathTextBox").Text = NewOptions().Path;
            Find<RadioButton>(mapping, "CredentialsRadioButton").Checked = true;
            Find<TextBox>(mapping, "UsernameTextBox").Text = NewOptions().Username;
            Find<TextBox>(mapping, "PasswordTextBox").Text = NewOptions().Password;
            Find<Button>(mapping, testFirst ? "TestConnectionButton" : "OkButton").PerformClick();
            try
            {
                PumpUntil(() => !mapping.Visible);
            }
            catch (TimeoutException error)
            {
                string windows = string.Join(", ", Application.OpenForms.Cast<Form>().Select(form => $"{form.Name} (modal={form.Modal})"));
                throw new TimeoutException($"{resolution}, testFirst={testFirst}, reviews={reviews}, calls={string.Join(',', fake.Calls)}, "
                    + $"status={Find<Label>(mapping, "OperationStatusLabel").Text}, windows={windows}", error);
            }
            if (eventFailure is not null)
                throw eventFailure;
            Equal(1, reviews);
            if (resolution == CredentialConflictResolution.Cancel)
            {
                Equal(DialogResult.Cancel, mapping.DialogResult);
                Equal(original, fake.Drives[25]);
                Check(!fake.Calls.Any(call => call.StartsWith("drop:")), "Cancel must not disconnect server resources.");
            }
            else
            {
                Equal(DialogResult.OK, mapping.DialogResult);
                Equal(NewOptions().Path, fake.Drives[25].NetworkPath);
                if (resolution == CredentialConflictResolution.UseExistingLogin)
                {
                    Check(fake.LastOptions!.UseExistingLogin, "The retry must select existing-session mode, not a guessed password.");
                    Equal(@"DOMAIN\Existing", fake.LastOptions.Username);
                    Equal(string.Empty, fake.LastOptions.Password);
                    Check(!fake.Calls.Any(call => call.StartsWith("drop:")), "Reusing a login must keep other server connections.");
                }
                else
                {
                    Check(!fake.LastOptions!.UseExistingLogin && fake.LastDisconnectWasForced, "Confirmed drop retries the original authentication.");
                    Equal(NewOptions().Username, fake.LastOptions.Username);
                    Equal(NewOptions().Password, fake.LastOptions.Password);
                    Check(fake.Calls.Any(call => call == "drop:Z:"), "The original selected letter can be released and safely remapped.");
                }
                Check(fake.Connections.Any(connection => connection.RemotePath == @"\\unrelated-server\share"), "Recovery must preserve unrelated servers.");
            }
        }
    }

    /// <summary>Creates a repeatable 1219 failure with the same native account-conflict information as production.</summary>
    private static Win32Exception ConflictError() => new(1219,
        "Map Z: failed (Windows error 1219): Multiple connections to a server or shared resource by the same user, using more than one user name, are not allowed. Disconnect all previous connections to the server or shared resource and try again.");

    /// <summary>Creates mapped and deviceless server connections plus a protected unrelated server for recovery tests.</summary>
    private static FakeNetworkDriveSystem ConflictSystem()
    {
        var fake = new FakeNetworkDriveSystem();
        fake.Set(new('L', DriveKind.Network, @"\\new-server\documents", @"\\new-server\documents", true));
        fake.Connections.AddRange([
            new("L:", @"\\new-server\documents", @"DOMAIN\Existing", "Connected", 2),
            new(null, @"\\new-server\IPC$", @"DOMAIN\Existing", "Connected", 0),
            new(null, @"\\unrelated-server\share", @"OTHER\Account", "Connected", 1)
        ]);
        return fake;
    }

    /// <summary>Verifies contextual help lifecycle, original content/formatting, owned modality, and external URLs.</summary>
    private static void CredentialsHelpAndRender(string[] args)
    {
        var fake = new FakeNetworkDriveSystem();
        using var dialog = new MappingDialog(new DriveMappingService(fake), fake.Drives[25]);
        ShowOffscreen(dialog);
        LinkLabel readMore = Find<LinkLabel>(dialog, "CredentialsReadMoreLink");
        Check(!readMore.Visible, "Help is hidden before a relevant error.");
        Find<TextBox>(dialog, "TargetPathTextBox").Text = @"\\server\share";
        const string accessDeniedMessage = "Connect to the network share failed (Windows error 5): Access is denied. Check the login and share permissions. Anonymous access requires a server and Windows policy that permit guest access.";
        dialog.SetErrorFeedback(new Win32Exception(5, accessDeniedMessage));
        Application.DoEvents();
        Check(readMore.Visible, "Windows error 5 should expose the help link.");
        Equal("Read more...", readMore.Text);
        Equal(accessDeniedMessage, Find<Label>(dialog, "OperationStatusLabel").Text);
        Check(readMore.Top >= Find<Label>(dialog, "OperationStatusLabel").Bottom,
            "The help link must follow the error text in the layout.");

        if (args.Contains("--render"))
        {
            Directory.CreateDirectory(Path.Combine("artifacts", "ui"));
            SaveRender(dialog, Path.Combine("artifacts", "ui", "mapping-access-denied.png"));
        }

        // Invoke the actual LinkClicked handler. The queued assertion runs inside ShowDialog's nested
        // message loop, then closes only the help window so the original editor remains usable.
        Exception? modalFailure = null;
        bool modalVerified = false;
        dialog.BeginInvoke((Action)(() =>
        {
            CredentialsHelpDialog? active = Application.OpenForms.OfType<CredentialsHelpDialog>().SingleOrDefault();
            try
            {
                Check(active is not null && active.Modal, "Read more must open a modal help dialog.");
                Check(active!.Owner == dialog, "The help dialog must belong to the mapping editor.");
                // ShowDialog disables the native owner window without changing Control.Enabled.
                Check(!IsWindowEnabled(dialog.Handle), "The editor must be disabled while its help dialog is open.");
                modalVerified = true;
            }
            catch (Exception error)
            {
                modalFailure = error;
            }
            finally
            {
                active?.Close();
            }
        }));
        ActivateLink(readMore);
        if (modalFailure is not null)
            throw modalFailure;
        Check(modalVerified && dialog.Enabled, "Closing help must return to the same enabled editor.");
        Equal(@"\\server\share", Find<TextBox>(dialog, "TargetPathTextBox").Text);

        dialog.SetErrorFeedback(new IOException("Wrapper", new Win32Exception(5, "Denied")));
        Check(readMore.Visible, "Wrapped mapping errors retain the access-denied help.");
        dialog.SetErrorFeedback(new UnauthorizedAccessException("Directory access denied"));
        Check(readMore.Visible, "Directory permission failures should also show help.");
        dialog.SetErrorFeedback(new Win32Exception(53, "Server not found"));
        Check(!readMore.Visible, "An unrelated network error must not show credential-access help.");
        dialog.SetErrorFeedback(new Win32Exception(5, "Denied"));
        Find<TextBox>(dialog, "TargetPathTextBox").Text = @"\\server\other";
        Check(!readMore.Visible, "Editing input clears stale contextual help.");
        dialog.SetErrorFeedback(new Win32Exception(5, "Denied"));
        Find<Button>(dialog, "TestConnectionButton").PerformClick();
        Check(!readMore.Visible, "Starting a new action hides the old error link immediately.");
        PumpUntil(() => Find<Label>(dialog, "OperationStatusLabel").Text.StartsWith("Connection succeeded"));
        Check(!readMore.Visible, "A successful test must not retain an old error link.");

        var opened = new List<Uri>();
        using var help = new CredentialsHelpDialog(opened.Add);
        ShowOffscreen(help);
        string actual = string.Join("\n\n", new[]
        {
            Find<RichTextBox>(help, "EveryoneParagraph").Text + " " + Find<LinkLabel>(help, "EveryoneParagraphDocumentationLink").Text,
            Find<RichTextBox>(help, "EmptyCredentialsParagraph").Text + " " + Find<LinkLabel>(help, "EmptyCredentialsParagraphDocumentationLink").Text,
            Find<RichTextBox>(help, "GuestParagraph").Text + " " + Find<LinkLabel>(help, "GuestParagraphDocumentationLink").Text,
            Find<RichTextBox>(help, "ConnectionTestParagraph").Text,
            Find<RichTextBox>(help, "OriginalFollowUpParagraph").Text
        });
        const string expected = """
            “Everyone” permissions do not automatically allow anonymous access. Windows normally excludes anonymous connections from the Everyone group. A share can therefore permit Everyone while still requiring a login or Guest identity. Microsoft documentation

            In this app, Anonymous currently sends an explicitly empty username and password to the Windows connection API. That differs from letting Windows use your current account: default credentials require null values. The “Anonymous” label therefore simplifies several distinct Windows authentication behaviors. Windows API documentation

            Arbitrary credentials succeeding suggests your server may be treating those connections as Guest. Windows has a “Guest only” sharing policy that maps local-account network logins to Guest. That can explain why a named login succeeds while an empty-credential attempt fails—but it remains a possible explanation until we see the error and server settings. Guest-only policy documentation

            The test actually connects and attempts to list the directory. Success means the resulting session can access that directory; it does not necessarily establish that the entered username/password were accepted as that named account.

            What exact Windows error number/message appears when you test Anonymous? That will help distinguish an empty-username rejection from an anonymous-access or Guest-policy restriction. No files were changed.
            """;
        Equal(expected.Replace("\r\n", "\n"), actual);
        RichTextBox firstParagraph = Find<RichTextBox>(help, "EveryoneParagraph");
        firstParagraph.Select(0, 10);
        Check(firstParagraph.SelectionFont?.Bold == true, "The original emphasis must use bold formatting.");
        firstParagraph.Select(0, 0);
        foreach (string name in new[] { "EveryoneParagraphDocumentationLink", "EmptyCredentialsParagraphDocumentationLink", "GuestParagraphDocumentationLink" })
            ActivateLink(Find<LinkLabel>(help, name));
        Equal(3, opened.Count);
        Equal("https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-let-everyone-permissions-apply-to-anonymous-users", opened[0].AbsoluteUri);
        Equal("https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w", opened[1].AbsoluteUri);
        Equal("https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-sharing-and-security-model-for-local-accounts", opened[2].AbsoluteUri);
        Check(fake.Calls.All(call => call == "test"), "Reading help must never alter mappings or launch Explorer.");

        if (args.Contains("--render"))
            SaveRender(help, Path.Combine("artifacts", "ui", "credentials-help.png"));
        Find<Button>(help, "CloseHelpButton").PerformClick();
        Check(!help.Visible, "The help Close button must close the explanation.");
        dialog.Close();
    }

    /// <summary>Raises a native LinkLabel's protected click event to test its real handler without moving the mouse.</summary>
    private static void ActivateLink(LinkLabel link)
    {
        MethodInfo handler = typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        handler.Invoke(link, [new LinkLabelLinkClickedEventArgs(link.Links[0])]);
    }

    /// <summary>Reads native window enablement to verify the actual modality enforced by ShowDialog.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    /// <summary>Verifies grid population/filtering and optionally renders both windows for human visual QA.</summary>
    private static void MainGridAndRender(string[] args)
    {
        var fake = ExistingMappingSystem();
        fake.Set(new('C', DriveKind.Occupied, "Taken - Hard Drive"));
        using var form = new MainForm(new DriveMappingService(fake));
        ShowOffscreen(form);
        DataGridView grid = Find<DataGridView>(form, "DrivesGrid");
        PumpUntil(() => grid.Rows.Count == 26 && Find<Button>(form, "RefreshButton").Enabled);
        Equal("A:", grid.Rows[0].Cells[0].Value);
        Equal("Z:", grid.Rows[25].Cells[0].Value);
        var filter = Find<CheckBox>(form, "OnlyAvailableCheckBox");
        Check(!filter.Checked, "The available-only filter starts unchecked.");
        filter.Checked = true;
        Equal(24, grid.Rows.Count);
        filter.Checked = false;
        Equal(26, grid.Rows.Count);

        if (args.Contains("--render"))
        {
            string output = Path.GetFullPath(Path.Combine("artifacts", "ui"));
            Directory.CreateDirectory(output);
            SaveRender(form, Path.Combine(output, "main.png"));
            using var dialog = new MappingDialog(new DriveMappingService(fake), OldMapping());
            ShowOffscreen(dialog);
            SaveRender(dialog, Path.Combine(output, "mapping-anonymous.png"));
            Find<RadioButton>(dialog, "CredentialsRadioButton").Checked = true;
            Find<TextBox>(dialog, "UsernameTextBox").Text = @"DOMAIN\alice";
            Find<TextBox>(dialog, "PasswordTextBox").Text = "example-password";
            SaveRender(dialog, Path.Combine(output, "mapping-credentials.png"));
            dialog.Close();
            Console.WriteLine($"     UI renders: {output}");
        }
        form.Close();
    }

    /// <summary>Verifies the requested icon order/availability, clicked drive, modal editor, keyboard action, and live scaling.</summary>
    private static void MainRowActionsAndScaling(string[] args)
    {
        var fake = ExistingMappingSystem();
        fake.Set(new('B', DriveKind.Network, @"\\server\share", @"\\server\share"));
        fake.Set(new('C', DriveKind.Occupied, "Taken - Hard Drive"));
        using var form = new MainForm(new DriveMappingService(fake));
        ShowOffscreen(form);
        DataGridView grid = Find<DataGridView>(form, "DrivesGrid");
        Button refresh = Find<Button>(form, "RefreshButton");
        PumpUntil(() => grid.Rows.Count == 26 && refresh.Enabled);
        Equal(BorderStyle.None, grid.BorderStyle);
        Equal(DataGridViewCellBorderStyle.None, grid.CellBorderStyle);
        Equal(DataGridViewHeaderBorderStyle.None, grid.ColumnHeadersBorderStyle);
        Equal(grid.ColumnCount - 1, grid.Columns["EditAction"].DisplayIndex);
        Equal(grid.ColumnCount - 2, grid.Columns["RemoveAction"].DisplayIndex);
        Equal(grid.ColumnCount - 3, grid.Columns["OpenAction"].DisplayIndex);
        Check(((DriveActionCell)grid.Rows[0].Cells["EditAction"]).ActionImage is not null
            && grid.Rows[0].Cells["OpenAction"].ContentBounds.IsEmpty
            && grid.Rows[0].Cells["RemoveAction"].ContentBounds.IsEmpty, "A free letter must have only the rightmost EDIT icon.");
        Check(((DriveActionCell)grid.Rows[25].Cells["OpenAction"]).ActionImage is not null
            && ((DriveActionCell)grid.Rows[25].Cells["RemoveAction"]).ActionImage is not null
            && ((DriveActionCell)grid.Rows[25].Cells["EditAction"]).ActionImage is not null, "A mapped row must show OPEN, REMOVE, then EDIT.");
        Check(grid.Rows[2].Cells["OpenAction"].ContentBounds.IsEmpty && grid.Rows[2].Cells["RemoveAction"].ContentBounds.IsEmpty
            && grid.Rows[2].Cells["EditAction"].ContentBounds.IsEmpty,
            "A reserved local drive must have no row actions.");
        Equal(string.Empty, refresh.Text);
        Check(refresh is StretchedIconButton { ActionImage: not null }
            && refresh.AccessibleName!.Contains("F5"), "Refresh must be an icon with an accessible shortcut description.");
        Equal(refresh.ClientSize.Width, refresh.ClientSize.Height);
        AssertRefreshMargin((StretchedIconButton)refresh);

        // Keep selection on A while activating the Z action, proving the clicked row controls the target.
        grid.CurrentCell = grid.Rows[0].Cells[0];
        ActivateGridAction(grid, 25, "OpenAction");
        PumpUntil(() => fake.Calls.Contains("explorer:Z") && refresh.Enabled);
        int previousCalls = fake.Calls.Count;
        ActivateGridAction(grid, 0, "OpenAction");
        ActivateGridAction(grid, 0, "RemoveAction");
        ActivateGridAction(grid, 2, "RemoveAction");
        ActivateGridAction(grid, 2, "EditAction");
        Equal(previousCalls, fake.Calls.Count);

        foreach (int row in new[] { 0, 25 })
        {
            Exception? modalFailure = null;
            bool editorVerified = false;
            form.BeginInvoke((Action)(() =>
            {
                MappingDialog? editor = Application.OpenForms.OfType<MappingDialog>().SingleOrDefault();
                try
                {
                    Check(editor is not null && editor.Modal && editor.Owner == form, "EDIT must open the owned mapping dialog.");
                    Equal(row == 0 ? string.Empty : OldMapping().NetworkPath!, Find<TextBox>(editor!, "TargetPathTextBox").Text);
                    editorVerified = true;
                }
                catch (Exception error)
                {
                    modalFailure = error;
                }
                finally
                {
                    editor?.Close();
                }
            }));
            ActivateGridAction(grid, row, "EditAction");
            PumpUntil(() => (editorVerified || modalFailure is not null) && refresh.Enabled);
            if (modalFailure is not null)
                throw modalFailure;
        }

        grid.CurrentCell = grid.Rows[25].Cells["OpenAction"];
        typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new KeyEventArgs(Keys.Enter)]);
        PumpUntil(() => fake.Calls.Count(call => call == "explorer:Z") == 2 && refresh.Enabled);
        fake.Set(new('D', DriveKind.Occupied, "Taken - Hard Drive"));
        refresh.PerformClick();
        PumpUntil(() => grid.Rows[3].Cells[1].Value?.ToString() == "Taken - Hard Drive" && refresh.Enabled);

        foreach (int height in new[] { 16, 48 })
        {
            foreach (DataGridViewRow row in grid.Rows)
                row.Height = height;
            Application.DoEvents();
            foreach (string action in new[] { "OpenAction", "RemoveAction", "EditAction" })
            {
                Equal(height, grid.Columns[action].Width);
                Equal(new Rectangle(2, 2, height - 4, height - 4), grid.Rows[1].Cells[action].ContentBounds);
            }
            if (args.Contains("--render"))
            {
                Directory.CreateDirectory(Path.Combine("artifacts", "ui"));
                SaveRender(form, Path.Combine("artifacts", "ui", $"main-icons-row-{height}.png"));
            }
        }
        // The source PNGs remain unchanged while control dimensions grow, like a display-scale change.
        form.Scale(new SizeF(1.5f, 1.5f));
        form.PerformLayout();
        Application.DoEvents();
        Equal(refresh.ClientSize.Width, refresh.ClientSize.Height);
        foreach (string action in new[] { "OpenAction", "RemoveAction", "EditAction" })
            Equal(new Rectangle(2, 2, grid.Columns[action].Width - 4, grid.Rows[1].Height - 4),
                grid.Rows[1].Cells[action].ContentBounds);
        AssertRefreshMargin((StretchedIconButton)refresh);
        if (args.Contains("--render"))
            SaveRender(form, Path.Combine("artifacts", "ui", "main-icons-scaled.png"));

        // REMOVE uses the clicked row independently of selection, and refreshes existing row objects without blinking.
        DataGridViewRow mappedRow = grid.Rows[25];
        int added = 0, removed = 0, enabledChanges = 0;
        grid.RowsAdded += (_, _) => added++;
        grid.RowsRemoved += (_, _) => removed++;
        grid.EnabledChanged += (_, _) => enabledChanges++;
        grid.CurrentCell = grid.Rows[0].Cells["EditAction"];
        ActivateGridAction(grid, 25, "RemoveAction");
        PumpUntil(() => refresh.Enabled && grid.Rows[25].Tag is DriveEntry { Kind: DriveKind.Free });
        Check(ReferenceEquals(mappedRow, grid.Rows[25]), "Removing a mapping must reuse the displayed row.");
        Equal("Available", grid.Rows[25].Cells[1].Value);
        Check(grid.Rows[25].Cells["OpenAction"].ContentBounds.IsEmpty && grid.Rows[25].Cells["RemoveAction"].ContentBounds.IsEmpty
            && !grid.Rows[25].Cells["EditAction"].ContentBounds.IsEmpty, "Removed mappings must immediately offer only EDIT.");
        Equal(DriveKind.Network, fake.Drives[1].Kind);
        Equal(0, added);
        Equal(0, removed);
        Equal(0, enabledChanges);
        // Keyboard activation of REMOVE has the same behavior.
        grid.CurrentCell = grid.Rows[1].Cells["RemoveAction"];
        typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new KeyEventArgs(Keys.Enter)]);
        PumpUntil(() => refresh.Enabled && grid.Rows[1].Tag is DriveEntry { Kind: DriveKind.Free });
        form.Close();
    }

    /// <summary>Checks actual toolbar painting leaves two clean background pixels on all sides, without native button edges.</summary>
    private static void AssertRefreshMargin(StretchedIconButton refresh)
    {
        Image? original = refresh.ActionImage;
        using var source = new Bitmap(8, 8);
        using (Graphics graphics = Graphics.FromImage(source))
            graphics.Clear(Color.Red);
        try
        {
            refresh.ActionImage = source;
            using var rendered = new Bitmap(refresh.Width, refresh.Height);
            refresh.DrawToBitmap(rendered, refresh.ClientRectangle);
            int background = refresh.Parent!.BackColor.ToArgb();
            for (int y = 0; y < rendered.Height; y++)
                for (int x = 0; x < rendered.Width; x++)
                    if (x < 2 || y < 2 || x >= rendered.Width - 2 || y >= rendered.Height - 2)
                        Equal(background, rendered.GetPixel(x, y).ToArgb());
            Equal(Color.Red.ToArgb(), rendered.GetPixel(rendered.Width / 2, rendered.Height / 2).ToArgb());
        }
        finally
        {
            refresh.ActionImage = original;
            refresh.Invalidate();
        }
    }

    /// <summary>Guards the blinking causes: whole-grid disabling and rebuilding unchanged rows after Explorer/editor/Refresh actions.</summary>
    private static void MainRefreshStability()
    {
        var fake = ExistingMappingSystem();
        using var form = new MainForm(new DriveMappingService(fake));
        ShowOffscreen(form);
        DataGridView grid = Find<DataGridView>(form, "DrivesGrid");
        Button refresh = Find<Button>(form, "RefreshButton");
        PumpUntil(() => grid.Rows.Count == 26 && refresh.Enabled);
        form.Height = 550;
        foreach (DataGridViewRow row in grid.Rows)
            row.Height = 48;
        grid.CurrentCell = grid.Rows[20].Cells["EditAction"];
        grid.FirstDisplayedScrollingRowIndex = 12;
        DataGridViewCell selected = grid.CurrentCell;
        int firstDisplayed = grid.FirstDisplayedScrollingRowIndex;
        DataGridViewRow[] rows = grid.Rows.Cast<DataGridViewRow>().ToArray();
        int added = 0, removed = 0, enabledChanges = 0;
        grid.RowsAdded += (_, _) => added++;
        grid.RowsRemoved += (_, _) => removed++;
        grid.EnabledChanged += (_, _) => enabledChanges++;
        int reads = fake.Calls.Count(call => call == "read");
        ActivateGridAction(grid, 25, "OpenAction");
        PumpUntil(() => fake.Calls.Contains("explorer:Z") && refresh.Enabled);
        Equal(reads, fake.Calls.Count(call => call == "read"));

        bool editorClosed = false;
        form.BeginInvoke((Action)(() =>
        {
            Application.OpenForms.OfType<MappingDialog>().Single().Close();
            editorClosed = true;
        }));
        ActivateGridAction(grid, 25, "EditAction");
        PumpUntil(() => editorClosed && refresh.Enabled);
        refresh.PerformClick();
        PumpUntil(() => refresh.Enabled);
        fake.Set(new('B', DriveKind.Network, @"\\changed-server\share", @"\\changed-server\share"));
        refresh.PerformClick();
        PumpUntil(() => refresh.Enabled && grid.Rows[1].Cells[1].Value?.ToString() == @"\\changed-server\share");
        Equal(0, added);
        Equal(0, removed);
        Equal(0, enabledChanges);
        Check(grid.CurrentCell == selected, "Actions/refresh must preserve the selected row and action column.");
        Equal(firstDisplayed, grid.FirstDisplayedScrollingRowIndex);
        for (int index = 0; index < rows.Length; index++)
        {
            Check(ReferenceEquals(rows[index], grid.Rows[index]), "Inventory refresh must reuse existing drive rows.");
            Equal(48, grid.Rows[index].Height);
        }
        form.Close();
    }

    /// <summary>Invokes a grid's real content-click event without moving the desktop pointer or launching real Explorer.</summary>
    private static void ActivateGridAction(DataGridView grid, int row, string column) =>
        typeof(DataGridView).GetMethod("OnCellContentClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(grid.Columns[column].Index, row)]);

    /// <summary>Creates a known persistent mapping snapshot used by replacement and recovery checks.</summary>
    private static DriveEntry OldMapping() => new('Z', DriveKind.Network, @"\\old-server\share (Disconnected)", @"\\old-server\share", true);

    /// <summary>Creates fake inventory with a previous mapping at Z:.</summary>
    private static FakeNetworkDriveSystem ExistingMappingSystem()
    {
        var fake = new FakeNetworkDriveSystem();
        fake.Set(OldMapping());
        return fake;
    }

    /// <summary>Creates a distinct target/login for safe replacement tests.</summary>
    private static ConnectionOptions NewOptions() => new(@"\\new-server\share\folder", true, @"DOMAIN\alice", "secret");

    /// <summary>Shows native forms outside the visible desktop while enabling normal event/async dispatch.</summary>
    private static void ShowOffscreen(Form form)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.Show();
        Application.DoEvents();
    }

    /// <summary>Processes the STA message loop until a UI action completes, with a bounded timeout.</summary>
    private static void PumpUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The UI operation did not complete within ten seconds.");
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    /// <summary>Finds a named real control without exposing implementation fields just for tests.</summary>
    private static T Find<T>(Control root, string name) where T : Control =>
        (T)root.Controls.Find(name, searchAllChildren: true).Single();

    /// <summary>Renders a complete WinForms window to PNG for layout inspection without screen capture.</summary>
    private static void SaveRender(Form form, string file)
    {
        form.PerformLayout();
        Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Throws a descriptive failure if a boolean expectation is not met.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>Checks value equality, including records and nullable values.</summary>
    private static void Equal<T>(T expected, T actual) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected '{expected}', received '{actual}'.");

    /// <summary>Checks the expected error type and returns it for inspection of recovery messages.</summary>
    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            return error;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
