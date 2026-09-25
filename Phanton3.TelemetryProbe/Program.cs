using Phanton3.TelemetryProbe;

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var workingDirectory = Directory.GetCurrentDirectory();

// Cada fonte tem seus próprios arquivos. Uma nova fonte pode ser adicionada aqui.
TelemetrySource[] sources =
[
    new(
        Name: "controller_2345",
        Host: "192.168.1.1",
        Port: 2345,
        InterfaceName: "Ethernet",
        CapturePath: Path.Combine(workingDirectory, "captures", "controller_2345.bin"),
        LogPath: Path.Combine(workingDirectory, "logs", "telemetry.log"))
];

try
{
    await Task.WhenAll(sources.Select(RunSourceAsync));
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
        await new TelemetryCapture(source).RunAsync(shutdown.Token);
    }
    catch
    {
        shutdown.Cancel();
        throw;
    }
}
