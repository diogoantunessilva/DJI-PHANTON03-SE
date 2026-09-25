using Phanton3.TelemetryProbe;

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var workingDirectory = Directory.GetCurrentDirectory();
var rcTestMode = args.Contains("--rc-test", StringComparer.OrdinalIgnoreCase);
var firstRcFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

// Cada fonte tem seus próprios arquivos. Uma nova fonte pode ser adicionada aqui.
TelemetrySource[] sources =
[
    new(
        Name: "controller_2345",
        Host: "192.168.1.1",
        Port: 2345,
        InterfaceName: "Wi-Fi",
        CapturePath: Path.Combine(workingDirectory, "captures", "controller_2345.bin"),
        LogPath: Path.Combine(workingDirectory, "logs", "telemetry.log"),
        FramesCapturePath: Path.Combine(workingDirectory, "captures", "duml_frames.bin"),
        FramesCsvPath: Path.Combine(workingDirectory, "logs", "duml_frames.csv"),
        CommandsSummaryPath: Path.Combine(workingDirectory, "logs", "commands_summary.csv"),
        RcChannelsCsvPath: Path.Combine(workingDirectory, "logs", "rc_channels.csv")),
    new(
        Name: "aircraft_5678",
        Host: "192.168.1.2",
        Port: 5678,
        InterfaceName: "Wi-Fi",
        CapturePath: Path.Combine(workingDirectory, "captures", "aircraft_5678.bin"),
        LogPath: Path.Combine(workingDirectory, "logs", "aircraft_telemetry.log"),
        FramesCapturePath: Path.Combine(workingDirectory, "captures", "aircraft_duml_frames.bin"),
        FramesCsvPath: Path.Combine(workingDirectory, "logs", "aircraft_frames.csv"),
        CommandsSummaryPath: Path.Combine(workingDirectory, "logs", "aircraft_commands_summary.csv"),
        RcChannelsCsvPath: null,
        IsAircraft: true)
];

try
{
    var captureTasks = sources.Select(RunSourceAsync).ToList();
    if (rcTestMode)
    {
        captureTasks.Add(RcTestTimeline.RunAsync(firstRcFrame.Task, shutdown,
            Path.Combine(workingDirectory, "logs")));
    }

    await Task.WhenAll(captureTasks);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Erro fatal: {exception}");
    Environment.ExitCode = 1;
}

async Task RunSourceAsync(TelemetrySource source)
{
    try
    {
        Action? onRcFrame = rcTestMode ? () => { firstRcFrame.TrySetResult(true); } : null;
        await new TelemetryCapture(source, onRcFrame, showFrames: !rcTestMode).RunAsync(shutdown.Token);
    }
    catch
    {
        shutdown.Cancel();
        throw;
    }
}
