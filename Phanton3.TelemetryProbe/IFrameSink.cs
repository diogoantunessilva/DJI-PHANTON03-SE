namespace Phanton3.TelemetryProbe;

internal interface IFrameSink : IAsyncDisposable
{
    Task<bool> WriteFrameAsync(DumlFrame frame, DateTimeOffset timestamp);
    Task FlushSummaryAsync();
}
