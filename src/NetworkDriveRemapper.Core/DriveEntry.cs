namespace NetworkDriveRemapper.Core;

/// <summary>Describes whether a letter is free, a known network mapping, or reserved.</summary>
public enum DriveKind
{
    /// <summary>No logical drive, DOS device, or remembered network mapping owns the letter.</summary>
    Free,
    /// <summary>A network mapping with a known target can be replaced.</summary>
    Network,
    /// <summary>A local volume, removable device, or other reservation owns the letter.</summary>
    Occupied
}

/// <summary>An immutable snapshot of one letter; it never contains authentication secrets.</summary>
/// <param name="Letter">Uppercase letter from A through Z.</param>
/// <param name="Kind">Availability classification.</param>
/// <param name="Description">Human-readable status for the grid.</param>
/// <param name="NetworkPath">Known UNC mapping target, otherwise null.</param>
/// <param name="IsPersistent">Whether Windows remembers this mapping for the next sign-in.</param>
public sealed record DriveEntry(char Letter, DriveKind Kind, string Description,
    string? NetworkPath = null, bool IsPersistent = false)
{
    /// <summary>Gets the Windows device name without a trailing backslash, for example Z:.</summary>
    public string DeviceName => $"{Letter}:";

    /// <summary>Gets whether this entry is safe to offer in the mapping dialog.</summary>
    public bool CanMap => Kind == DriveKind.Free || (Kind == DriveKind.Network && NetworkPath is not null);
}
