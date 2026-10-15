using System.ComponentModel;

namespace NetworkDriveRemapper.Core;

/// <summary>Recognizes Windows credential-session conflicts without matching localized error strings.</summary>
public static class CredentialConflict
{
    /// <summary>Checks native error 1219 through mapping/rollback exception wrappers.</summary>
    public static bool IsConflict(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is Win32Exception { NativeErrorCode: 1219 }
                || current.HResult == unchecked((int)0x800704C3))
                return true;
        return false;
    }
}
