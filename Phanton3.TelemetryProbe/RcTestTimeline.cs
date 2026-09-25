using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal static class RcTestTimeline
{
    private static readonly string[] Phases =
    [
        "Tudo parado",
        "Stick direito esquerda/direita",
        "Stick direito cima/baixo",
        "Stick esquerdo cima/baixo",
        "Stick esquerdo esquerda/direita",
        "Roda do gimbal"
    ];

    public static async Task RunAsync(
        Task firstRcFrame,
        CancellationTokenSource shutdown,
        string logsDirectory)
    {
        try
        {
            Console.WriteLine("MODO TESTE RC: drone no chão, hélices removidas. Aguardando quadros RC válidos...");
            await firstRcFrame.WaitAsync(shutdown.Token);

            if (Console.IsInputRedirected)
            {
                throw new InvalidOperationException("O teste RC precisa de um terminal interativo para iniciar com Enter.");
            }

            Console.WriteLine("Quadros RC detectados. Prepare-se e pressione Enter para iniciar os 60 segundos.");
            await Task.Run(() => Console.ReadLine()).WaitAsync(shutdown.Token);

            Directory.CreateDirectory(logsDirectory);
            var markerPath = Path.Combine(logsDirectory,
                $"rc_test_markers_{DateTimeOffset.Now:yyyyMMdd_HHmmss_fff}.csv");
            await using var markers = new StreamWriter(markerPath, append: false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            await markers.WriteLineAsync("timestamp,elapsed_seconds,phase");

            var timer = Stopwatch.StartNew();
            for (var phase = 0; phase < Phases.Length; phase++)
            {
                var due = TimeSpan.FromSeconds(phase * 10);
                var remaining = due - timer.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, shutdown.Token);
                }

                var timestamp = DateTimeOffset.Now;
                await markers.WriteLineAsync($"{timestamp.ToString("O", CultureInfo.InvariantCulture)},{phase * 10},{Phases[phase]}");
                Console.WriteLine($"TESTE RC {phase * 10:D2}–{(phase + 1) * 10:D2} s: {Phases[phase]}");
            }

            var untilEnd = TimeSpan.FromSeconds(60) - timer.Elapsed;
            if (untilEnd > TimeSpan.Zero)
            {
                await Task.Delay(untilEnd, shutdown.Token);
            }

            await markers.WriteLineAsync($"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)},60,Fim");
            Console.WriteLine($"TESTE RC concluído. Marcadores: {markerPath}");
            shutdown.Cancel();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Console.WriteLine("Teste RC interrompido.");
        }
        catch
        {
            shutdown.Cancel();
            throw;
        }
    }
}
