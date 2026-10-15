using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NetworkDriveRemapper.Core;

/// <summary>Unicode Win32 declarations. All connection APIs return an error code directly.</summary>
internal static class NativeMethods
{
    internal const int Success = 0;
    internal const int MoreData = 234;
    internal const int ConnectionUnavailable = 1201;
    internal const int NotConnected = 2250;
    internal const int UpdateProfile = 1;
    internal const int Temporary = 4;

    /// <summary>Layout of NETRESOURCEW; unused provider/description fields are null.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NetworkResource
    {
        internal int Scope;
        internal int Type;
        internal int DisplayType;
        internal int Usage;
        internal string? LocalName;
        internal string? RemoteName;
        internal string? Comment;
        internal string? Provider;
    }

    /// <summary>Returns the local/connected logical-drive bitmask (A is bit zero).</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetLogicalDrives();

    /// <summary>Classifies a drive root without reading its files or volume label.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint GetDriveType(string rootPathName);

    /// <summary>Detects DOS device reservations, including SUBST letters.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);

    /// <summary>Retrieves a live or unavailable remembered mapping for one letter.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    /// <summary>Creates either a letter mapping or a deviceless session with explicit credentials.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetAddConnection2(ref NetworkResource resource, string? password,
        string? username, int flags);

    /// <summary>Disconnects one letter or deviceless share; false prevents forced closure of open files.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    /// <summary>Gets provider-specific text immediately after ERROR_EXTENDED_ERROR.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetGetLastError(out int error, StringBuilder description, int descriptionLength,
        StringBuilder provider, int providerLength);

    /// <summary>Layout of USE_INFO_2. The password pointer is deliberately never read or marshaled to managed text.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UseInfo2
    {
        internal IntPtr LocalName;
        internal IntPtr RemoteName;
        internal IntPtr Password;
        internal uint Status;
        internal uint AssignmentType;
        internal uint ReferenceCount;
        internal uint UseCount;
        internal IntPtr Username;
        internal IntPtr Domain;
    }

    /// <summary>Reads the current user's SMB uses at level 2, with paginated identity/status metadata.</summary>
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    internal static extern int NetUseEnum(string? serverName, int level, out IntPtr buffer,
        uint preferredLength, out uint entriesRead, out uint totalEntries, ref uint resumeHandle);

    /// <summary>Releases every buffer returned by NetUseEnum, including partial-result buffers.</summary>
    [DllImport("netapi32.dll")]
    internal static extern int NetApiBufferFree(IntPtr buffer);

    /// <summary>Starts enumeration of resources in the caller's current network logon session.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetOpenEnum(int scope, int type, int usage, IntPtr resource, out IntPtr enumeration);

    /// <summary>Reads NETRESOURCEW entries, reporting a larger required buffer on ERROR_MORE_DATA.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetEnumResource(IntPtr enumeration, ref uint count, IntPtr buffer, ref uint bufferSize);

    /// <summary>Closes a WNet resource-enumeration handle.</summary>
    [DllImport("mpr.dll")]
    internal static extern int WNetCloseEnum(IntPtr enumeration);

    /// <summary>Reads the account used by a mapped device or remote network resource.</summary>
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    internal static extern int WNetGetUser(string resource, StringBuilder username, ref int length);

    /// <summary>Clones the current token into a separate outbound network logon session (type 9).</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LogonUser(string username, string? domain, string password,
        int logonType, int logonProvider, out SafeAccessTokenHandle token);
}
