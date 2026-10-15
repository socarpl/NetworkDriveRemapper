namespace NetworkDriveRemapper.Core;

/// <summary>Coordinates safe replacement, preflight checks, and best-effort rollback of a mapping.</summary>
/// <param name="system">Windows adapter or an in-memory test adapter.</param>
public sealed class DriveMappingService(INetworkDriveSystem system)
{
    /// <summary>Returns alphabetically ordered snapshots, with optional filtering to unused letters.</summary>
    /// <param name="onlyAvailable">True to hide every assigned letter, including network mappings.</param>
    public IReadOnlyList<DriveEntry> GetDrives(bool onlyAvailable = false) => system.GetDrives()
        .Where(entry => !onlyAvailable || entry.Kind == DriveKind.Free).OrderBy(entry => entry.Letter).ToArray();

    /// <summary>Tests the dialog's inputs without touching the selected drive or normal SMB sessions.</summary>
    public void TestConnection(ConnectionOptions options) => system.TestConnection(options);

    /// <summary>Opens the entered path directly in Explorer without testing or authenticating to it.</summary>
    /// <param name="path">Exact target-path text, independent of the dialog's login fields.</param>
    public void OpenLocation(string path) => system.OpenLocation(path);

    /// <summary>Opens the successful mapping in the current user's Explorer.</summary>
    public void OpenDrive(char letter) => system.OpenDrive(letter);

    /// <summary>Removes only the selected, unchanged network mapping, with an optional explicitly confirmed forced retry.</summary>
    /// <param name="expected">Mapped-drive snapshot displayed in the main window.</param>
    /// <param name="force">False initially; true only after the user confirms retrying Windows error 2401.</param>
    /// <remarks>Windows also removes the remembered mapping. Unrelated letters and server connections are left alone.</remarks>
    public void Remove(DriveEntry expected, bool force = false)
    {
        if (expected.Kind != DriveKind.Network)
            throw new InvalidOperationException("Only an existing network drive mapping can be removed.");
        DriveEntry current = EnsureUnchanged(expected);
        system.Unmap(current.Letter, force);
    }

    /// <summary>Reads connection accounts and resources for the server involved in error 1219.</summary>
    public ServerConnectionReport GetServerConnections(string targetPath) => system.GetServerConnections(targetPath);

    /// <summary>Rebases a failed operation only when the letter is free or still owns its original mapping.</summary>
    /// <remarks>A failed replacement may have removed its old mapping when rollback was unsuccessful.</remarks>
    public DriveEntry GetRetryDrive(DriveEntry original)
    {
        DriveEntry current = system.GetDrives().Single(entry => entry.Letter == original.Letter);
        if (!current.CanMap || (current.Kind != DriveKind.Free
            && (original.Kind != DriveKind.Network
                || !string.Equals(current.NetworkPath, original.NetworkPath, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("The drive letter is now owned by a different device or mapping. Cancel and refresh the list.");
        return current;
    }

    /// <summary>Drops only the reviewed server's connections after confirmation, refusing an incomplete or changed list.</summary>
    /// <param name="confirmed">Exact connection snapshot shown in the confirmation dialog.</param>
    /// <remarks>Successful earlier disconnections cannot be undone if a later connection refuses to disconnect.</remarks>
    public void DisconnectServer(ServerConnectionReport confirmed)
    {
        if (!confirmed.CanDisconnect || confirmed.Connections.Any(connection => !confirmed.ContainsServer(connection.RemotePath)))
            throw new InvalidOperationException("Windows did not provide a complete connection list for this server. No connections were dropped.");
        ServerConnectionReport current = system.GetServerConnections(confirmed.TargetPath);
        string[] reviewed = confirmed.Connections.Select(connection => connection.Identity)
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] live = current.Connections.Select(connection => connection.Identity)
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!current.CanDisconnect || !reviewed.SequenceEqual(live, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The server's connections changed after they were listed. No connections were dropped. Retry to review the current list.");

        int completed = 0;
        foreach (NetworkConnection connection in current.Connections.OrderBy(connection => connection.LocalName is null ? 1 : 0))
        {
            try
            {
                system.DisconnectConnection(connection, force: true);
                completed++;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Could not disconnect {connection.LocalName ?? connection.RemotePath}: {error.Message}\n\n"
                    + $"{completed} connection(s) were already disconnected. Mapping was not retried. Refresh before continuing.", error);
            }
        }
        ServerConnectionReport remaining = system.GetServerConnections(confirmed.TargetPath);
        if (remaining.Warnings.Count > 0 || remaining.Connections.Count > 0)
            throw new InvalidOperationException("Some connections remain, were reopened by another application, or could not be verified. Mapping was not retried. Close applications using the server and try again.");
    }

    /// <summary>Rechecks ownership, tests the target, and replaces only the originally selected mapping.</summary>
    /// <param name="expected">Snapshot seen by the user when opening the dialog.</param>
    /// <param name="options">Validated target and credentials.</param>
    /// <remarks>
    /// The new mapping is persistent. An existing mapping is disconnected without force. If mapping
    /// fails after disconnection, the old target and persistence are restored where Windows credentials
    /// allow it. Windows cannot return the old password, so restoration is explicitly best-effort.
    /// </remarks>
    public void Apply(DriveEntry expected, ConnectionOptions options)
    {
        EnsureUnchanged(expected);
        // Preflight happens before disconnecting anything, in a separate authentication session.
        system.TestConnection(options);
        // A server test can take time. Check again so a concurrently mounted local drive is protected.
        DriveEntry current = EnsureUnchanged(expected);
        if (current.Kind == DriveKind.Network)
            system.Unmap(current.Letter);

        try
        {
            system.Map(current.Letter, options, persistent: true);
        }
        catch (Exception mappingError)
        {
            if (current.Kind != DriveKind.Network)
                throw;
            try
            {
                system.Restore(current);
            }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException($"The new mapping failed: {mappingError.Message}\n\n"
                    + $"The previous mapping to {current.NetworkPath} could not be restored: {restoreError.Message}\n"
                    + "Refresh the list and map the previous target again using its original credentials.", mappingError);
            }
            throw new InvalidOperationException($"The new mapping failed: {mappingError.Message}\n\n"
                + $"The previous mapping to {current.NetworkPath} was restored.", mappingError);
        }
    }

    /// <summary>Stops stale-dialog operations when another process has changed the letter.</summary>
    /// <returns>The latest matching snapshot.</returns>
    private DriveEntry EnsureUnchanged(DriveEntry expected)
    {
        if (expected.Letter is < 'A' or > 'Z' || !expected.CanMap)
            throw new InvalidOperationException("This drive letter is reserved and cannot be mapped.");
        DriveEntry current = system.GetDrives().Single(entry => entry.Letter == expected.Letter);
        if (!current.CanMap || current.Kind != expected.Kind
            || !string.Equals(current.NetworkPath, expected.NetworkPath, StringComparison.OrdinalIgnoreCase)
            || current.IsPersistent != expected.IsPersistent)
            throw new InvalidOperationException("This drive letter changed while the dialog was open. Cancel and refresh the list.");
        return current;
    }
}
