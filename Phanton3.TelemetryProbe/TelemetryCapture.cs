using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal sealed class TelemetryCapture(TelemetrySource source)
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private const int HexPreviewLength = 32;

    public async Task RunAsync(CancellationToken shutdownToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(source.CapturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(source.LogPath)!);

        await using var capture = new FileStream(
            source.CapturePath, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 64 * 1024, options: FileOptions.Asynchronous);
        await using var logFile = new FileStream(
            source.LogPath, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, options: FileOptions.Asynchronous);
        await using var log = new StreamWriter(logFile, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        await LogAsync(log, "Iniciando captura somente de leitura. Pressione Ctrl+C para encerrar.");

        var buffer = new byte[8192];

        while (!shutdownToken.IsCancellationRequested)
        {
            try
            {
                var localAddress = ResolveLocalAddress();
                using var client = new TcpClient(new IPEndPoint(localAddress, 0));
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                connectionTimeout.CancelAfter(ConnectionTimeout);

                await LogAsync(log, $"Conectando a {source.Host}:{source.Port} pela interface {source.InterfaceName} ({localAddress}).");
                await client.ConnectAsync(source.Host, source.Port, connectionTimeout.Token);
                await LogAsync(log, "Conectado.");

                using var stream = client.GetStream();
                while (!shutdownToken.IsCancellationRequested)
                {
                    var count = await stream.ReadAsync(buffer, shutdownToken);
                    if (count == 0)
                    {
                        await LogAsync(log, "Conexão encerrada pelo equipamento.");
                        break;
                    }

                    try
                    {
                        await capture.WriteAsync(buffer.AsMemory(0, count), shutdownToken);
                        await capture.FlushAsync(shutdownToken);
                    }
                    catch (IOException exception)
                    {
                        throw new InvalidOperationException("Falha ao salvar a captura bruta.", exception);
                    }

                    var preview = Convert.ToHexString(buffer.AsSpan(0, Math.Min(count, HexPreviewLength)));
                    await LogAsync(log, $"{count} bytes recebidos | hex[0..{Math.Min(count, HexPreviewLength)}]: {preview}");
                }
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                await LogAsync(log, $"Timeout de conexão após {ConnectionTimeout.TotalSeconds:0} segundos.");
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                await LogAsync(log, $"Conexão perdida: {exception.Message}");
            }

            try
            {
                await Task.Delay(ReconnectDelay, shutdownToken);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                break;
            }
        }

        await LogAsync(log, "Captura encerrada.");
    }

    private IPAddress ResolveLocalAddress()
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => string.Equals(item.Name, source.InterfaceName, StringComparison.OrdinalIgnoreCase));

        if (adapter is null || adapter.OperationalStatus != OperationalStatus.Up)
        {
            throw new IOException($"A interface {source.InterfaceName} não está conectada.");
        }

        var destination = IPAddress.Parse(source.Host);
        var address = adapter.GetIPProperties().UnicastAddresses
            .FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork
                && item.IPv4Mask is not null
                && IsOnSameSubnet(item.Address, destination, item.IPv4Mask));

        return address?.Address
            ?? throw new IOException($"A interface {source.InterfaceName} não tem um IPv4 na rede de {source.Host}.");
    }

    private static bool IsOnSameSubnet(IPAddress local, IPAddress destination, IPAddress mask)
    {
        var localBytes = local.GetAddressBytes();
        var destinationBytes = destination.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (localBytes.Length != destinationBytes.Length || localBytes.Length != maskBytes.Length)
        {
            return false;
        }

        for (var index = 0; index < localBytes.Length; index++)
        {
            if ((localBytes[index] & maskBytes[index]) != (destinationBytes[index] & maskBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private async Task LogAsync(StreamWriter log, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{source.Name}] {message}";
        Console.WriteLine(line);

        try
        {
            await log.WriteLineAsync(line);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Falha ao salvar o log textual.", exception);
        }
    }
}
