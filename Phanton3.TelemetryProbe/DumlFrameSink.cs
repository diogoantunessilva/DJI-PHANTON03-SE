using System.Globalization;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal sealed class DumlFrameSink : IFrameSink
{
    private const string FramesHeader = "timestamp,len,version,header_crc8,sender,receiver,sequence,flags,cmdSet,cmdId,payloadLen,payloadHex,crc16_received,CRC8_OK,CRC16_OK";
    private const string SummaryHeader = "sender,receiver,cmdSet,cmdId,count";
    private const string RcHeader = "timestamp,aileron,elevator,throttle,rudder,gyro,wheel,status1,status2";
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TelemetrySource _source;
    private readonly FileStream _frames;
    private readonly StreamWriter _csv;
    private readonly StreamWriter _rcCsv;
    private readonly bool _showFrames;
    private readonly Dictionary<(byte Sender, byte Receiver, byte CommandSet, byte CommandId), long> _counts;
    private DateTimeOffset _lastSummaryWrite = DateTimeOffset.MinValue;
    private bool _summaryDirty = true;

    private DumlFrameSink(TelemetrySource source, bool showFrames)
    {
        var rcPath = source.RcChannelsCsvPath ?? throw new ArgumentException("Fonte RC sem caminho do CSV de canais.", nameof(source));
        _source = source;
        _showFrames = showFrames;
        _counts = LoadCounts(source.FramesCsvPath);
        var newCsv = !File.Exists(source.FramesCsvPath) || new FileInfo(source.FramesCsvPath).Length == 0;
        var newRcCsv = !File.Exists(rcPath) || new FileInfo(rcPath).Length == 0;

        _frames = new FileStream(source.FramesCapturePath, FileMode.Append, FileAccess.Write,
            FileShare.Read, bufferSize: 64 * 1024, options: FileOptions.Asynchronous);
        var csvFile = new FileStream(source.FramesCsvPath, FileMode.Append, FileAccess.Write,
            FileShare.Read, bufferSize: 4096, options: FileOptions.Asynchronous);
        _csv = new StreamWriter(csvFile, Utf8) { AutoFlush = true };
        var rcFile = new FileStream(rcPath, FileMode.Append, FileAccess.Write,
            FileShare.Read, bufferSize: 4096, options: FileOptions.Asynchronous);
        _rcCsv = new StreamWriter(rcFile, Utf8) { AutoFlush = true };

        if (newCsv)
        {
            _csv.WriteLine(FramesHeader);
        }
        if (newRcCsv)
        {
            _rcCsv.WriteLine(RcHeader);
        }
    }

    public static async Task<DumlFrameSink> OpenAsync(TelemetrySource source, bool showFrames = true)
    {
        var rcPath = source.RcChannelsCsvPath ?? throw new ArgumentException("Fonte RC sem caminho do CSV de canais.", nameof(source));
        Directory.CreateDirectory(Path.GetDirectoryName(source.FramesCapturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(source.FramesCsvPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(source.CommandsSummaryPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(rcPath)!);

        var sink = new DumlFrameSink(source, showFrames);
        await sink.FlushSummaryAsync();
        return sink;
    }

    public async Task<bool> WriteFrameAsync(DumlFrame frame, DateTimeOffset timestamp)
    {
        try
        {
            await _frames.WriteAsync(frame.Bytes);
            await _frames.FlushAsync();

            var csvLine = string.Join(',',
                timestamp.ToString("O", CultureInfo.InvariantCulture),
                frame.TotalLength.ToString(CultureInfo.InvariantCulture),
                frame.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                Hex(frame.HeaderCrc8),
                Hex(frame.Sender),
                Hex(frame.Receiver),
                frame.Sequence.ToString(CultureInfo.InvariantCulture),
                Hex(frame.Flags),
                Hex(frame.CommandSet),
                Hex(frame.CommandId),
                frame.PayloadLength.ToString(CultureInfo.InvariantCulture),
                frame.PayloadHex,
                $"0x{frame.ReceivedCrc16:X4}",
                frame.Crc8Ok ? "true" : "false",
                frame.Crc16Ok ? "true" : "false");
            await _csv.WriteLineAsync(csvLine);

            var semantic = DumlSemanticDecoder.Decode(frame);
            if (semantic?.Channels is { } channels)
            {
                var rcLine = string.Join(',',
                    timestamp.ToString("O", CultureInfo.InvariantCulture),
                    channels.Aileron.ToString(CultureInfo.InvariantCulture),
                    channels.Elevator.ToString(CultureInfo.InvariantCulture),
                    channels.Throttle.ToString(CultureInfo.InvariantCulture),
                    channels.Rudder.ToString(CultureInfo.InvariantCulture),
                    channels.GyroValue.ToString(CultureInfo.InvariantCulture),
                    channels.WheelInfo.ToString(CultureInfo.InvariantCulture),
                    channels.Status1.ToString(CultureInfo.InvariantCulture),
                    channels.Status2.ToString(CultureInfo.InvariantCulture));
                await _rcCsv.WriteLineAsync(rcLine);
            }

            var key = (frame.Sender, frame.Receiver, frame.CommandSet, frame.CommandId);
            _counts.TryGetValue(key, out var current);
            _counts[key] = current + 1;
            _summaryDirty = true;

            if (_showFrames)
            {
                var header = $"{timestamp:O} | source={_source.Name} | len={frame.TotalLength} | src={Hex(frame.Sender)} | dst={Hex(frame.Receiver)} | seq={frame.Sequence} | flags={Hex(frame.Flags)} | cmdSet={Hex(frame.CommandSet)} | cmdId={Hex(frame.CommandId)} | payloadLen={frame.PayloadLength} | CRC8={Hex(frame.HeaderCrc8)} CRC8_OK={frame.Crc8Ok.ToString().ToLowerInvariant()} | CRC16=0x{frame.ReceivedCrc16:X4} CRC16_OK={frame.Crc16Ok.ToString().ToLowerInvariant()}";
                var details = semantic?.Channels is { } displayChannels
                    ? Environment.NewLine + RcChannelDisplay.Format(displayChannels)
                    : semantic is not null ? " | " + semantic.DisplayText : "";
                Console.WriteLine(header + details);
            }

            if (timestamp - _lastSummaryWrite >= SummaryInterval)
            {
                await FlushSummaryAsync();
            }

            return semantic?.Channels is not null;
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Falha ao salvar os quadros DUML.", exception);
        }
    }

    public async Task FlushSummaryAsync()
    {
        if (!_summaryDirty)
        {
            return;
        }

        var lines = new StringBuilder().AppendLine(SummaryHeader);
        foreach (var entry in _counts.OrderBy(item => item.Key.Sender)
                     .ThenBy(item => item.Key.Receiver)
                     .ThenBy(item => item.Key.CommandSet)
                     .ThenBy(item => item.Key.CommandId))
        {
            lines.Append(Hex(entry.Key.Sender)).Append(',')
                .Append(Hex(entry.Key.Receiver)).Append(',')
                .Append(Hex(entry.Key.CommandSet)).Append(',')
                .Append(Hex(entry.Key.CommandId)).Append(',')
                .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        var temporaryPath = _source.CommandsSummaryPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, lines.ToString(), Utf8);
            File.Move(temporaryPath, _source.CommandsSummaryPath, overwrite: true);
            _summaryDirty = false;
            _lastSummaryWrite = DateTimeOffset.Now;
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Falha ao salvar o resumo de comandos.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushSummaryAsync();
        }
        finally
        {
            await _rcCsv.DisposeAsync();
            await _csv.DisposeAsync();
            await _frames.DisposeAsync();
        }
    }

    private static Dictionary<(byte Sender, byte Receiver, byte CommandSet, byte CommandId), long> LoadCounts(string path)
    {
        var counts = new Dictionary<(byte, byte, byte, byte), long>();
        if (!File.Exists(path))
        {
            return counts;
        }

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var columns = line.Split(',');
            if (columns.Length < 15
                || !TryParseHex(columns[4], out var sender)
                || !TryParseHex(columns[5], out var receiver)
                || !TryParseHex(columns[8], out var commandSet)
                || !TryParseHex(columns[9], out var commandId))
            {
                continue;
            }

            var key = (sender, receiver, commandSet, commandId);
            counts.TryGetValue(key, out var current);
            counts[key] = current + 1;
        }

        return counts;
    }

    private static string Hex(byte value) => $"0x{value:X2}";

    private static bool TryParseHex(string value, out byte result)
    {
        result = 0;
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }
}
