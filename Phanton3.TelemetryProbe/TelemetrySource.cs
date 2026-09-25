namespace Phanton3.TelemetryProbe;

internal sealed record TelemetrySource(
    string Name,
    string Host,
    int Port,
    string InterfaceName,
    string CapturePath,
    string LogPath,
    string FramesCapturePath,
    string FramesCsvPath,
    string CommandsSummaryPath,
    string? RcChannelsCsvPath,
    bool IsAircraft = false);
