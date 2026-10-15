namespace NetworkDriveRemapper.Core;

/// <summary>A current-user network connection snapshot, containing identity information but no password.</summary>
/// <param name="LocalName">Mapped device name such as Z:, or null for a connection without a drive letter.</param>
/// <param name="RemotePath">UNC resource reported by Windows.</param>
/// <param name="Login">Reported connection account, or null if the provider cannot identify it.</param>
/// <param name="Status">Readable connection state.</param>
/// <param name="OpenCount">Reported resource references, or null when the provider does not expose them.</param>
public sealed record NetworkConnection(string? LocalName, string RemotePath, string? Login,
    string Status, uint? OpenCount = null)
{
    /// <summary>Gets a stable identifier used to detect changes after the user confirms disconnection.</summary>
    public string Identity => $"{LocalName ?? string.Empty}|{RemotePath}|{Login ?? string.Empty}";
}

/// <summary>Connections to the selected server, with discovery warnings and a reusable login when known.</summary>
/// <param name="TargetPath">Validated proposed UNC directory.</param>
/// <param name="Connections">Mapped and deviceless connections belonging to the current Windows logon.</param>
/// <param name="Warnings">Discovery failures; unsafe bulk disconnection is disabled when listing is incomplete.</param>
public sealed record ServerConnectionReport(string TargetPath, IReadOnlyList<NetworkConnection> Connections,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Gets the UNC server name without resolving aliases or unrelated hosts.</summary>
    public string Server => GetServer(TargetPath);

    /// <summary>Gets a single unambiguous known account suitable for an existing-session retry.</summary>
    public string? ExistingLogin
    {
        get
        {
            string[] logins = Connections.Where(connection => connection.Status == "Connected")
                .Select(connection => connection.Login).OfType<string>().Where(login => !string.IsNullOrWhiteSpace(login))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return logins.Length == 1 ? logins[0] : null;
        }
    }

    /// <summary>Gets whether the user can review a complete, nonempty list before bulk disconnection.</summary>
    public bool CanDisconnect => Connections.Count > 0 && Warnings.Count == 0;

    /// <summary>Extracts the server component of a UNC path; returns empty for non-UNC provider resources.</summary>
    public static string GetServer(string path) => path.StartsWith(@"\\", StringComparison.Ordinal)
        ? path[2..].Split('\\', 2)[0].TrimEnd('.') : string.Empty;

    /// <summary>Tests exact, case-insensitive server identity so a similar hostname is never disconnected.</summary>
    public bool ContainsServer(string path) => string.Equals(Server, GetServer(path), StringComparison.OrdinalIgnoreCase);
}
