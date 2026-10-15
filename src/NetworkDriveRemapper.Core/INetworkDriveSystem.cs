namespace NetworkDriveRemapper.Core;

/// <summary>Boundary between mapping policy and Windows, also allowing safe deterministic tests.</summary>
public interface INetworkDriveSystem
{
    /// <summary>Reads all 26 letters without attempting to access the contents of a network drive.</summary>
    IReadOnlyList<DriveEntry> GetDrives();
    /// <summary>Verifies directory access using an isolated new login or a selected existing desktop session.</summary>
    void TestConnection(ConnectionOptions options);
    /// <summary>Maps one device using the requested credentials and persistence setting.</summary>
    void Map(char letter, ConnectionOptions options, bool persistent);
    /// <summary>Disconnects only this letter and removes its remembered mapping.</summary>
    /// <param name="letter">Mapped drive letter to disconnect.</param>
    /// <param name="force">False normally; true only for a user-confirmed retry after Windows error 2401.</param>
    void Unmap(char letter, bool force = false);
    /// <summary>Attempts to restore a previous mapping using Windows' existing/default credentials.</summary>
    void Restore(DriveEntry previous);
    /// <summary>Passes the entered path directly to Explorer without validating connectivity or credentials.</summary>
    /// <param name="path">Exact target-path text; Explorer handles access and authentication.</param>
    void OpenLocation(string path);
    /// <summary>Opens an already mapped letter in Explorer without putting credentials in arguments.</summary>
    void OpenDrive(char letter);
    /// <summary>Lists current-logon mapped and deviceless connections to the proposed UNC server.</summary>
    ServerConnectionReport GetServerConnections(string targetPath);
    /// <summary>Disconnects one reviewed connection; force is used only after an explicit bulk-disconnect confirmation.</summary>
    void DisconnectConnection(NetworkConnection connection, bool force);
}
