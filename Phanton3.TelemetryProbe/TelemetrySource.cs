namespace Phanton3.TelemetryProbe;

internal sealed record TelemetrySource(
    string Name,
    string Host,
    int Port,
    string CapturePath,
    string LogPath);
