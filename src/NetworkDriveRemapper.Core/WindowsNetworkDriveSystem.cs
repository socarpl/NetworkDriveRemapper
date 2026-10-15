using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace NetworkDriveRemapper.Core;

/// <summary>Windows 10/11 adapter for drive discovery, SMB authentication, and Explorer.</summary>
/// <remarks>Run unelevated so mappings belong to the same logon session as desktop Explorer.</remarks>
public sealed partial class WindowsNetworkDriveSystem : INetworkDriveSystem
{
    /// <inheritdoc />
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        uint driveMask = NativeMethods.GetLogicalDrives();
        if (driveMask == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate logical drives.");

        // Remembered offline mappings may not appear in GetLogicalDrives. Read, never edit, HKCU.
        using RegistryKey? remembered = Registry.CurrentUser.OpenSubKey(@"Network");
        var entries = new List<DriveEntry>(26);
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            string device = $"{letter}:";
            uint type = NativeMethods.GetDriveType(device + @"\");
            bool logicalDriveExists = (driveMask & (1u << (letter - 'A'))) != 0;
            var dosTarget = new StringBuilder(32768);
            bool dosDeviceExists = NativeMethods.QueryDosDevice(device, dosTarget, dosTarget.Capacity) != 0;
            int dosError = dosDeviceExists ? 0 : Marshal.GetLastWin32Error();
            using RegistryKey? saved = remembered?.OpenSubKey(letter.ToString());
            string? savedPath = saved?.GetValue("RemotePath") as string;
            (int error, string? remotePath) = ReadNetworkTarget(device);

            // A real local volume takes precedence over a stale remembered network entry.
            if ((logicalDriveExists || dosDeviceExists) && type is 2 or 3 or 5 or 6)
            {
                string description = dosTarget.ToString().StartsWith(@"\??\", StringComparison.Ordinal)
                    ? "Taken - Virtual / SUBST Drive" : DescribeLocalType(type);
                entries.Add(new(letter, DriveKind.Occupied, description));
            }
            else if (!string.IsNullOrWhiteSpace(remotePath) || !string.IsNullOrWhiteSpace(savedPath))
            {
                string target = remotePath ?? savedPath!;
                bool disconnected = error != NativeMethods.Success;
                entries.Add(new(letter, DriveKind.Network, target + (disconnected ? " (Disconnected)" : ""),
                    target, savedPath is not null));
            }
            else if (logicalDriveExists || dosDeviceExists || type == 4 || error == NativeMethods.ConnectionUnavailable)
                entries.Add(new(letter, DriveKind.Occupied,
                    type == 4 || error == NativeMethods.ConnectionUnavailable
                        ? "Taken - Network Drive (target unavailable)" : "Taken - Reserved Device"));
            else if (dosError != 2 || error is not (NativeMethods.NotConnected or 1200))
                // Unexpected failures are not proof that a letter is free. Never offer it for replacement.
                entries.Add(new(letter, DriveKind.Occupied, "Unavailable - Windows could not verify this letter"));
            else
                entries.Add(new(letter, DriveKind.Free, "Available"));
        }
        return entries;
    }

    /// <inheritdoc />
    public void TestConnection(ConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UseExistingLogin)
        {
            // Reusing a session must happen in the desktop logon, not the isolated test token.
            ServerConnectionReport current = GetServerConnections(options.Path);
            if (!string.Equals(current.ExistingLogin, options.Username, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The existing Windows login changed or is no longer connected. Retry to review the current connections.");
            VerifyDirectory(options.Path);
            return;
        }
        // NEW_CREDENTIALS creates a new network logon without validating credentials. The subsequent
        // WNet call and directory enumeration perform the actual remote authentication/access check.
        string username = options.UseCredentials ? options.Username : Environment.UserName;
        string? domain = options.UseCredentials ? null : Environment.UserDomainName;
        int slash = username.IndexOf('\\');
        if (slash >= 0)
        {
            domain = username[..slash];
            username = username[(slash + 1)..];
        }
        if (!NativeMethods.LogonUser(username, domain, options.Password, 9, 3, out var token))
            throw CreateError(Marshal.GetLastWin32Error(), "Create an isolated connection test");
        using (token)
        {
            WindowsIdentity.RunImpersonated(token, () =>
            {
                ConnectShare(options);
                try
                {
                    VerifyDirectory(options.Path);
                }
                finally
                {
                    // This share belongs exclusively to the temporary logon, never desktop Explorer.
                    int result = NativeMethods.WNetCancelConnection2(options.ShareRoot, 0, force: false);
                    if (result != NativeMethods.Success && result != NativeMethods.NotConnected)
                        throw CreateError(result, "Close the temporary test connection");
                }
            });
        }
    }

    /// <inheritdoc />
    public void Map(char letter, ConnectionOptions options, bool persistent)
    {
        ValidateLetter(letter);
        var resource = new NativeMethods.NetworkResource { Type = 1, LocalName = $"{letter}:", RemoteName = options.Path };
        int result = NativeMethods.WNetAddConnection2(ref resource,
            options.UseExistingLogin ? null : options.Password, options.Username,
            persistent ? NativeMethods.UpdateProfile : NativeMethods.Temporary);
        if (result != NativeMethods.Success)
            throw CreateError(result, $"Map {letter}:");
    }

    /// <inheritdoc />
    public void Unmap(char letter, bool force = false)
    {
        ValidateLetter(letter);
        int result = NativeMethods.WNetCancelConnection2($"{letter}:", NativeMethods.UpdateProfile, force);
        if (result != NativeMethods.Success)
            throw CreateError(result, $"Disconnect {letter}:");
    }

    /// <inheritdoc />
    public void Restore(DriveEntry previous)
    {
        ValidateLetter(previous.Letter);
        if (previous.NetworkPath is null)
            throw new ArgumentException("The previous network target is unknown.", nameof(previous));
        var resource = new NativeMethods.NetworkResource
        {
            Type = 1, LocalName = previous.DeviceName, RemoteName = previous.NetworkPath
        };
        // Null means Windows default credentials. The failed NEW target's credentials must not be
        // used to restore an unrelated OLD target, and Windows cannot reveal the old stored password.
        int result = NativeMethods.WNetAddConnection2(ref resource, null, null,
            previous.IsPersistent ? NativeMethods.UpdateProfile : NativeMethods.Temporary);
        if (result != NativeMethods.Success)
            throw CreateError(result, $"Restore {previous.DeviceName}");
    }

    /// <inheritdoc />
    public void OpenLocation(string path)
    {
        // Browsing is independent of mapping input validation and connection tests. Let Explorer
        // handle the exact entered path and any Windows authentication or access errors itself.
        LaunchExplorer(path);
    }

    /// <inheritdoc />
    public void OpenDrive(char letter)
    {
        ValidateLetter(letter);
        LaunchExplorer($"{letter}:\\");
    }

    /// <summary>Reads a network target with a resized buffer when Windows returns ERROR_MORE_DATA.</summary>
    private static (int Error, string? Path) ReadNetworkTarget(string device)
    {
        int length = 512;
        var buffer = new StringBuilder(length);
        int result = NativeMethods.WNetGetConnection(device, buffer, ref length);
        if (result == NativeMethods.MoreData)
        {
            buffer = new StringBuilder(length);
            result = NativeMethods.WNetGetConnection(device, buffer, ref length);
        }
        string? path = result is NativeMethods.Success or NativeMethods.ConnectionUnavailable
            && buffer.Length > 0 ? buffer.ToString() : null;
        return (result, path);
    }

    /// <summary>Creates a nonpersistent, deviceless connection using empty or explicit credentials.</summary>
    private static void ConnectShare(ConnectionOptions options)
    {
        var resource = new NativeMethods.NetworkResource { Type = 1, RemoteName = options.ShareRoot };
        int result = NativeMethods.WNetAddConnection2(ref resource, options.Password, options.Username, NativeMethods.Temporary);
        if (result != NativeMethods.Success)
            throw CreateError(result, "Connect to the network share");
    }

    /// <summary>Checks that the target is a directory and that its entries can actually be listed.</summary>
    private static void VerifyDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
            throw new IOException("The target exists but is not a directory.");
        // Directory.Exists hides access errors. Starting enumeration preserves the useful exception
        // and also succeeds for an empty folder. The enumerator is disposed before session cleanup.
        using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        entries.MoveNext();
    }

    /// <summary>Launches Explorer with one safely separated argument and no authentication secrets.</summary>
    private static void LaunchExplorer(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add(path);
        using Process? process = Process.Start(start);
        if (process is null)
            throw new InvalidOperationException("Windows could not start Explorer.");
    }

    /// <summary>Produces clear labels for Win32 drive types, even when media is absent.</summary>
    private static string DescribeLocalType(uint type) => type switch
    {
        2 => "Taken - Removable Drive",
        3 => "Taken - Hard Drive",
        5 => "Taken - CD/DVD Drive",
        6 => "Taken - RAM Disk",
        _ => "Taken - Reserved Device"
    };

    /// <summary>Rejects invalid device names before any Windows mutation.</summary>
    private static void ValidateLetter(char letter)
    {
        if (letter is < 'A' or > 'Z')
            throw new ArgumentOutOfRangeException(nameof(letter), "Drive letters must be uppercase A through Z.");
    }

    /// <summary>Translates native failures, adding actionable hints without echoing passwords.</summary>
    private static Win32Exception CreateError(int code, string action)
    {
        string explanation = new Win32Exception(code).Message;
        if (code == 1208)
        {
            var detail = new StringBuilder(2048);
            var provider = new StringBuilder(256);
            if (NativeMethods.WNetGetLastError(out _, detail, detail.Capacity, provider, provider.Capacity) == 0)
                explanation = detail.ToString();
        }
        string hint = code switch
        {
            1219 => " Windows already has a connection to this server with another login. Close its files and disconnect that conflicting connection yourself, then retry.",
            5 or 86 or 1326 => " Check the login and share permissions. Anonymous access requires a server and Windows policy that permit guest access.",
            53 or 67 => @" Check the \\server\share path, network connectivity, and VPN.",
            2401 or 2404 => " Close files and Explorer windows using this drive, then retry.",
            _ => string.Empty
        };
        return new Win32Exception(code, $"{action} failed (Windows error {code}): {explanation}{hint}");
    }
}
