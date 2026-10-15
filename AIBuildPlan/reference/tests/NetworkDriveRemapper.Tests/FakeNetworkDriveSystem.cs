using NetworkDriveRemapper.Core;

namespace NetworkDriveRemapper.Tests;

/// <summary>In-memory Windows boundary that records operations without changing real mappings.</summary>
internal sealed class FakeNetworkDriveSystem : INetworkDriveSystem
{
    internal List<DriveEntry> Drives { get; } = Enumerable.Range('A', 26)
        .Select(letter => new DriveEntry((char)letter, DriveKind.Free, "Available")).ToList();
    internal List<string> Calls { get; } = [];
    internal Exception? TestError { get; set; }
    internal Exception? MapError { get; set; }
    internal Exception? UnmapError { get; set; }
    internal Queue<Exception?> UnmapFailures { get; } = new();
    internal List<(char Letter, bool Force)> UnmapAttempts { get; } = [];
    internal Exception? RestoreError { get; set; }
    internal Action? DuringTest { get; set; }
    internal ConnectionOptions? LastOptions { get; private set; }
    internal string? LastOpenedPath { get; private set; }
    internal bool LastPersistent { get; private set; }
    internal DriveEntry? LastRestored { get; private set; }
    internal List<NetworkConnection> Connections { get; } = [];
    internal List<string> DiscoveryWarnings { get; } = [];
    internal Queue<Exception?> MappingFailures { get; } = new();
    internal Queue<Exception?> TestFailures { get; } = new();
    internal Exception? DisconnectError { get; set; }
    internal bool LastDisconnectWasForced { get; private set; }
    internal Queue<Exception?> DisconnectFailures { get; } = new();
    private readonly Dictionary<char, NetworkConnection?> disconnectedMappings = new();

    /// <summary>Returns immutable copies so a test can model changes between discovery calls.</summary>
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        Calls.Add("read");
        return Drives.ToArray();
    }

    /// <summary>Simulates successful or failing remote preflight and optional concurrent ownership changes.</summary>
    public void TestConnection(ConnectionOptions options)
    {
        Calls.Add("test");
        LastOptions = options;
        DuringTest?.Invoke();
        Exception? error = TestFailures.Count > 0 ? TestFailures.Dequeue() : TestError;
        if (error is not null)
            throw error;
    }

    /// <summary>Records exact credentials and persistence, then updates the fake letter or fails.</summary>
    public void Map(char letter, ConnectionOptions options, bool persistent)
    {
        Calls.Add("map");
        LastOptions = options;
        LastPersistent = persistent;
        Exception? error = MappingFailures.Count > 0 ? MappingFailures.Dequeue() : MapError;
        if (error is not null)
            throw error;
        Set(new(letter, DriveKind.Network, options.Path, options.Path, persistent));
        Connections.RemoveAll(connection => connection.LocalName == $"{letter}:");
        Connections.Add(new($"{letter}:", options.Path, options.UseCredentials ? options.Username : null, "Connected"));
    }

    /// <summary>Records the exact letter/force flag and releases only that fake mapping, or throws a queued/default error.</summary>
    public void Unmap(char letter, bool force = false)
    {
        Calls.Add("unmap");
        UnmapAttempts.Add((letter, force));
        Exception? error = UnmapFailures.Count > 0 ? UnmapFailures.Dequeue() : UnmapError;
        if (error is not null)
            throw error;
        disconnectedMappings[letter] = Connections.FirstOrDefault(connection => connection.LocalName == $"{letter}:");
        Connections.RemoveAll(connection => connection.LocalName == $"{letter}:");
        Set(new(letter, DriveKind.Free, "Available"));
    }

    /// <summary>Records the original snapshot and either restores it or simulates unavailable old credentials.</summary>
    public void Restore(DriveEntry previous)
    {
        Calls.Add("restore");
        LastRestored = previous;
        if (RestoreError is not null)
            throw RestoreError;
        Set(previous);
        if (disconnectedMappings.GetValueOrDefault(previous.Letter) is NetworkConnection connection)
            Connections.Add(connection);
    }

    /// <summary>Records Explorer browsing without launching a process or authenticating to a real server.</summary>
    public void OpenLocation(string path)
    {
        Calls.Add("browse");
        LastOpenedPath = path;
    }

    /// <summary>Records the letter Explorer would open after mapping.</summary>
    public void OpenDrive(char letter) => Calls.Add($"explorer:{letter}");

    /// <summary>Returns fake server-scoped metadata and warnings without any privileged Windows query.</summary>
    public ServerConnectionReport GetServerConnections(string targetPath)
    {
        Calls.Add("connections");
        string normalized = ConnectionOptions.NormalizePath(targetPath);
        string server = ServerConnectionReport.GetServer(normalized);
        return new(normalized, Connections.Where(connection => string.Equals(ServerConnectionReport.GetServer(connection.RemotePath),
            server, StringComparison.OrdinalIgnoreCase)).ToArray(), DiscoveryWarnings.ToArray());
    }

    /// <summary>Records confirmed disconnection, releases fake mapped letters, or simulates a failed drop.</summary>
    public void DisconnectConnection(NetworkConnection connection, bool force)
    {
        Calls.Add("drop:" + (connection.LocalName ?? connection.RemotePath));
        LastDisconnectWasForced = force;
        Exception? error = DisconnectFailures.Count > 0 ? DisconnectFailures.Dequeue() : DisconnectError;
        if (error is not null)
            throw error;
        Connections.RemoveAll(current => current.Identity == connection.Identity);
        if (connection.LocalName is { Length: 2 } device && device[1] == ':' && device[0] is >= 'A' and <= 'Z')
            Set(new(device[0], DriveKind.Free, "Available"));
    }

    /// <summary>Replaces one letter, used to simulate physical disks, offline mappings, and races.</summary>
    internal void Set(DriveEntry entry) => Drives[entry.Letter - 'A'] = entry;
}
