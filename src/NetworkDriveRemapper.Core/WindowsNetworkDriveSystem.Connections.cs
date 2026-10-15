using System.Runtime.InteropServices;
using System.Text;

namespace NetworkDriveRemapper.Core;

/// <summary>Current-logon connection discovery and explicitly confirmed server-scoped disconnection.</summary>
public sealed partial class WindowsNetworkDriveSystem
{
    /// <inheritdoc />
    public ServerConnectionReport GetServerConnections(string targetPath)
    {
        string normalized = ConnectionOptions.NormalizePath(targetPath);
        string server = ServerConnectionReport.GetServer(normalized);
        var connections = new Dictionary<string, NetworkConnection>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        foreach ((string source, Func<IReadOnlyList<NetworkConnection>> read) in new[]
        {
            ("SMB connections", (Func<IReadOnlyList<NetworkConnection>>)ReadSmbConnections),
            ("Connected network resources", (Func<IReadOnlyList<NetworkConnection>>)ReadConnectedResources),
            ("Mapped network drives", (Func<IReadOnlyList<NetworkConnection>>)ReadMappedConnections)
        })
        {
            try
            {
                foreach (NetworkConnection connection in read())
                {
                    if (!string.Equals(server, ServerConnectionReport.GetServer(connection.RemotePath), StringComparison.OrdinalIgnoreCase))
                        continue;
                    string key = $"{connection.LocalName ?? string.Empty}|{connection.RemotePath.TrimEnd('\\')}";
                    if (connections.TryGetValue(key, out NetworkConnection? previous))
                        connections[key] = previous with
                        {
                            Login = connection.Login ?? previous.Login,
                            Status = connection.Status == "Connected" ? connection.Status : previous.Status,
                            OpenCount = previous.OpenCount ?? connection.OpenCount
                        };
                    else
                        connections[key] = connection;
                }
            }
            catch (Exception error)
            {
                // Retain useful partial identity information, but prevent a bulk drop from an incomplete list.
                warnings.Add($"{source} could not be fully read: {error.Message}");
            }
        }
        return new(normalized, connections.Values.OrderBy(connection => connection.RemotePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.LocalName, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }

    /// <inheritdoc />
    public void DisconnectConnection(NetworkConnection connection, bool force)
    {
        string name = connection.LocalName ?? connection.RemotePath;
        if (connection.LocalName is not null)
        {
            (int error, string? currentPath) = ReadNetworkTarget(name);
            // Recheck each device immediately before canceling it. Another process may have changed
            // a later letter after the server-wide review, or while earlier resources were dropped.
            if (currentPath is null && name.Length == 2 && name[1] == ':')
            {
                using var saved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Network\{name[0]}");
                currentPath = saved?.GetValue("RemotePath") as string;
            }
            if (currentPath is null && error is NativeMethods.NotConnected or 1200)
                return;
            if (!string.Equals(currentPath?.TrimEnd('\\'), connection.RemotePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} changed after the connection review. It was not disconnected.");
        }
        int flags = connection.LocalName is null ? 0 : NativeMethods.UpdateProfile;
        int result = NativeMethods.WNetCancelConnection2(name, flags, force);
        // Canceling a device can also release a deviceless entry for the same share. That already-
        // disconnected entry needs no further work; other failures are surfaced and stop the batch.
        if (result is not (NativeMethods.Success or NativeMethods.NotConnected))
            throw CreateError(result, $"Disconnect {name}");
    }

    /// <summary>Enumerates paginated USE_INFO_2 entries without administrator privileges or reading passwords.</summary>
    private static IReadOnlyList<NetworkConnection> ReadSmbConnections()
    {
        var connections = new List<NetworkConnection>();
        uint resume = 0;
        int result;
        do
        {
            uint previousResume = resume;
            result = NativeMethods.NetUseEnum(null, 2, out IntPtr buffer, uint.MaxValue, out uint count, out _, ref resume);
            try
            {
                if (result is not (NativeMethods.Success or NativeMethods.MoreData))
                    throw CreateError(result, "Read current-user SMB connections");
                int stride = Marshal.SizeOf<NativeMethods.UseInfo2>();
                for (uint index = 0; index < count; index++)
                {
                    var use = Marshal.PtrToStructure<NativeMethods.UseInfo2>(IntPtr.Add(buffer, checked((int)index * stride)));
                    string? remote = Marshal.PtrToStringUni(use.RemoteName);
                    if (string.IsNullOrWhiteSpace(remote))
                        continue;
                    string? local = EmptyToNull(Marshal.PtrToStringUni(use.LocalName));
                    string? login = ReadConnectionLogin(local ?? remote)
                        ?? CombineLogin(Marshal.PtrToStringUni(use.Domain), Marshal.PtrToStringUni(use.Username));
                    connections.Add(new(local, remote, login, DescribeConnectionStatus(use.Status), use.ReferenceCount));
                }
                if (result == NativeMethods.MoreData && count == 0 && resume == previousResume)
                    throw new InvalidOperationException("Windows could not advance the SMB connection enumeration.");
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    NativeMethods.NetApiBufferFree(buffer);
            }
        } while (result == NativeMethods.MoreData);
        return connections;
    }

    /// <summary>Enumerates provider resources, including current-user connections omitted from the A–Z grid.</summary>
    private static IReadOnlyList<NetworkConnection> ReadConnectedResources()
    {
        int result = NativeMethods.WNetOpenEnum(1, 0, 0, IntPtr.Zero, out IntPtr enumeration);
        if (result != NativeMethods.Success)
            throw CreateError(result, "Enumerate connected network resources");
        IntPtr buffer = IntPtr.Zero;
        var connections = new List<NetworkConnection>();
        try
        {
            uint capacity = 16384;
            buffer = Marshal.AllocHGlobal((int)capacity);
            while (true)
            {
                uint count = uint.MaxValue;
                uint size = capacity;
                result = NativeMethods.WNetEnumResource(enumeration, ref count, buffer, ref size);
                if (result == 259) // ERROR_NO_MORE_ITEMS
                    break;
                if (result == NativeMethods.MoreData)
                {
                    uint next = Math.Max(size, checked(capacity * 2));
                    if (next > 16 * 1024 * 1024)
                        throw new InvalidOperationException("The network provider requested an unexpectedly large enumeration buffer.");
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    capacity = next;
                    buffer = Marshal.AllocHGlobal((int)capacity);
                    continue;
                }
                if (result != NativeMethods.Success)
                    throw CreateError(result, "Read connected network resources");
                if (count == 0)
                    break;
                int stride = Marshal.SizeOf<NativeMethods.NetworkResource>();
                for (uint index = 0; index < count; index++)
                {
                    var resource = Marshal.PtrToStructure<NativeMethods.NetworkResource>(IntPtr.Add(buffer, checked((int)index * stride)));
                    if (string.IsNullOrWhiteSpace(resource.RemoteName))
                        continue;
                    string? local = EmptyToNull(resource.LocalName);
                    connections.Add(new(local, resource.RemoteName,
                        ReadConnectionLogin(local ?? resource.RemoteName), "Connected"));
                }
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            NativeMethods.WNetCloseEnum(enumeration);
        }
        return connections;
    }

    /// <summary>Supplements enumeration with mapped and remembered letters, including unavailable mappings.</summary>
    private IReadOnlyList<NetworkConnection> ReadMappedConnections() => GetDrives()
        .Where(drive => drive.Kind == DriveKind.Network && drive.NetworkPath is not null)
        .Select(drive => new NetworkConnection(drive.DeviceName, drive.NetworkPath!, ReadConnectionLogin(drive.DeviceName),
            drive.Description.EndsWith(" (Disconnected)", StringComparison.Ordinal) ? "Disconnected" : "Connected")).ToArray();

    /// <summary>Reads a provider-reported account, retrying its character buffer when required.</summary>
    private static string? ReadConnectionLogin(string name)
    {
        int length = 512;
        var buffer = new StringBuilder(length);
        int result = NativeMethods.WNetGetUser(name, buffer, ref length);
        if (result == NativeMethods.MoreData)
        {
            buffer = new StringBuilder(length);
            result = NativeMethods.WNetGetUser(name, buffer, ref length);
        }
        return result == NativeMethods.Success ? EmptyToNull(buffer.ToString()) : null;
    }

    /// <summary>Combines a domain and unqualified username while preserving UPN or already-qualified names.</summary>
    private static string? CombineLogin(string? domain, string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;
        return !string.IsNullOrWhiteSpace(domain) && !username.Contains('\\') && !username.Contains('@')
            ? $"{domain}\\{username}" : username;
    }

    /// <summary>Normalizes absent provider device/account fields to null.</summary>
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Translates USE_INFO_2 state codes for the connection-review grid.</summary>
    private static string DescribeConnectionStatus(uint status) => status switch
    {
        0 => "Connected", 1 => "Paused", 2 => "Disconnected", 3 => "Disconnected",
        4 => "Network error", 5 => "Connecting", 6 => "Reconnecting", _ => "Unknown"
    };
}
