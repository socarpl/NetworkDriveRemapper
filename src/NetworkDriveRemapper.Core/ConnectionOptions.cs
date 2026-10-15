namespace NetworkDriveRemapper.Core;

/// <summary>Validated connection input kept in memory only; passwords are never serialized.</summary>
public sealed class ConnectionOptions
{
    /// <summary>Initializes normalized UNC input and either explicit or anonymous credentials.</summary>
    /// <param name="path">A UNC share or directory, for example \\server\share\folder.</param>
    /// <param name="useCredentials">True to authenticate with the supplied username/password.</param>
    /// <param name="username">DOMAIN\user, user@domain, or a server-local account name.</param>
    /// <param name="password">Password; spaces are preserved and an empty password is allowed.</param>
    /// <exception cref="ArgumentException">The path or selected username is invalid.</exception>
    public ConnectionOptions(string path, bool useCredentials, string? username = null, string? password = null)
        : this(path, useCredentials, username, password, useExistingLogin: false) { }

    /// <summary>Creates validated options for explicit, anonymous, or password-free existing-session authentication.</summary>
    private ConnectionOptions(string path, bool useCredentials, string? username, string? password, bool useExistingLogin)
    {
        Path = NormalizePath(path);
        UseCredentials = useCredentials;
        Username = useCredentials ? username?.Trim() ?? string.Empty : string.Empty;
        Password = useCredentials ? password ?? string.Empty : string.Empty;
        UseExistingLogin = useExistingLogin;
        if (useCredentials && string.IsNullOrWhiteSpace(Username))
            throw new ArgumentException("Enter a username when Credentials is selected.", nameof(username));
        if (Username.Contains('\0') || Password.Contains('\0'))
            throw new ArgumentException("Credentials cannot contain a null character.");
    }

    /// <summary>Gets the validated target directory.</summary>
    public string Path { get; }
    /// <summary>Gets whether named credentials are used instead of empty anonymous credentials.</summary>
    public bool UseCredentials { get; }
    /// <summary>Gets the explicit username; anonymous access supplies an empty string.</summary>
    public string Username { get; }
    /// <summary>Gets the password for the native call; never log this property.</summary>
    public string Password { get; }
    /// <summary>Gets whether Windows should reuse the named established login without supplying a new password.</summary>
    public bool UseExistingLogin { get; }

    /// <summary>Creates a retry using a Windows-reported existing account; its password is neither obtained nor stored.</summary>
    public static ConnectionOptions ForExistingLogin(string path, string login) =>
        new(path, true, login, null, useExistingLogin: true);
    /// <summary>Gets the share root used to create a deviceless SMB session before browsing.</summary>
    public string ShareRoot => @"\\" + string.Join('\\', Path[2..].Split('\\').Take(2));

    /// <summary>Validates UNC syntax without contacting a server or accepting shell arguments.</summary>
    /// <param name="path">User input, optionally surrounded by whitespace.</param>
    /// <returns>A UNC path without trailing backslashes.</returns>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string value = path.Trim().TrimEnd('\\');
        if (!value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith(@"\\?\", StringComparison.Ordinal)
            || value.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException(@"Enter a network path in the form \\server\share or \\server\share\folder.");

        string[] segments = value[2..].Split('\\');
        if (segments.Length < 2 || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.Any(character => character < 32 || "<>:\"/|?*".Contains(character))))
            throw new ArgumentException("The network path contains an invalid server, share, or directory name.");
        return value;
    }

    /// <summary>Returns a safe diagnostic representation without username or password.</summary>
    public override string ToString() => Path;
}
