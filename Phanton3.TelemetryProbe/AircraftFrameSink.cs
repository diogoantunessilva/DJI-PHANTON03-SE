using System.Globalization;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal sealed class AircraftFrameSink : IFrameSink
{
    private const string FramesHeader = "timestamp,source,length,sender,receiver,sequence,flags,cmdSet,cmdId,payloadLength,payloadHex,crc8Ok,crc16Ok";
    private const string SummaryHeader = "sender,receiver,cmdSet,cmdId,count,payloadLength,frequencyHz,firstTimestamp,lastTimestamp";
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TelemetrySource _source;
    private readonly FileStream _frames;
    private readonly StreamWriter _csv;
    private readonly bool _showFrames;
    private readonly Dictionary<(byte Sender, byte Receiver, byte CommandSet, byte CommandId), CommandStats> _stats;
    private DateTimeOffset _lastSummaryWrite = DateTimeOffset.MinValue;
    private bool _summaryDirty = true;

    private AircraftFrameSink(TelemetrySource source, bool showFrames)
    {
        _source = source;
        _showFrames = showFrames;
        _stats = LoadStats(source.FramesCsvPath);
        var newCsv = !File.Exists(source.FramesCsvPath) || new FileInfo(source.FramesCsvPath).Length == 0;

        _frames = new FileStream(source.FramesCapturePath, FileMode.Append, FileAccess.Write,
            FileShare.Read, bufferSize: 64 * 1024, options: FileOptions.Asynchronous);
        var csvFile = new FileStream(source.FramesCsvPath, FileMode.Append, FileAccess.Write,
            FileShare.Read, bufferSize: 4096, options: FileOptions.Asynchronous);
        _csv = new StreamWriter(csvFile, Utf8) { AutoFlush = true };

        if (newCsv)
        {
            _csv.WriteLine(FramesHeader);
        }
    }

    public static async Task<AircraftFrameSink> OpenAsync(TelemetrySource source, bool showFrames = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(source.FramesCapturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(source.FramesCsvPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(source.CommandsSummaryPath)!);

        var sink = new AircraftFrameSink(source, showFrames);
        await sink.FlushSummaryAsync();
        return sink;
    }

    public async Task<bool> WriteFrameAsync(DumlFrame frame, DateTimeOffset timestamp)
    {
        try
        {
            await _frames.WriteAsync(frame.Bytes);
            await _frames.FlushAsync();

            await _csv.WriteLineAsync(string.Join(',',
                timestamp.ToString("O", CultureInfo.InvariantCulture),
                _source.Name,
                frame.TotalLength.ToString(CultureInfo.InvariantCulture),
                Hex(frame.Sender),
                Hex(frame.Receiver),
                frame.Sequence.ToString(CultureInfo.InvariantCulture),
                Hex(frame.Flags),
                Hex(frame.CommandSet),
                Hex(frame.CommandId),
                frame.PayloadLength.ToString(CultureInfo.InvariantCulture),
                frame.PayloadHex,
                frame.Crc8Ok ? "true" : "false",
                frame.Crc16Ok ? "true" : "false"));

            var key = (frame.Sender, frame.Receiver, frame.CommandSet, frame.CommandId);
            if (!_stats.TryGetValue(key, out var stats))
            {
                stats = new CommandStats();
                _stats.Add(key, stats);
            }
            stats.Add(timestamp, frame.PayloadLength);
            _summaryDirty = true;

            if (_showFrames)
            {
                Console.WriteLine($"{timestamp:O} | source={_source.Name} | len={frame.TotalLength} | src={Hex(frame.Sender)} | dst={Hex(frame.Receiver)} | seq={frame.Sequence} | flags={Hex(frame.Flags)} | cmdSet={Hex(frame.CommandSet)} | cmdId={Hex(frame.CommandId)} | payloadLen={frame.PayloadLength} | CRC8_OK={frame.Crc8Ok.ToString().ToLowerInvariant()} | CRC16_OK={frame.Crc16Ok.ToString().ToLowerInvariant()}");
            }

            if (timestamp - _lastSummaryWrite >= SummaryInterval)
            {
                await FlushSummaryAsync();
            }

            return false;
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Falha ao salvar os quadros DUML da aeronave.", exception);
        }
    }

    public async Task FlushSummaryAsync()
    {
        if (!_summaryDirty)
        {
            return;
        }

        var lines = new StringBuilder().AppendLine(SummaryHeader);
        foreach (var entry in _stats.OrderBy(item => item.Key.Sender)
                     .ThenBy(item => item.Key.Receiver)
                     .ThenBy(item => item.Key.CommandSet)
                     .ThenBy(item => item.Key.CommandId))
        {
            var stats = entry.Value;
            var seconds = (stats.LastTimestamp - stats.FirstTimestamp).TotalSeconds;
            var frequency = stats.Count > 1 && seconds > 0
                ? (stats.Count - 1) / seconds
                : 0;

            lines.Append(Hex(entry.Key.Sender)).Append(',')
                .Append(Hex(entry.Key.Receiver)).Append(',')
                .Append(Hex(entry.Key.CommandSet)).Append(',')
                .Append(Hex(entry.Key.CommandId)).Append(',')
                .Append(stats.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(string.Join('|', stats.PayloadLengths.Order())).Append(',')
                .Append(frequency.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(stats.FirstTimestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(stats.LastTimestamp.ToString("O", CultureInfo.InvariantCulture)).AppendLine();
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
            throw new InvalidOperationException("Falha ao salvar o resumo de comandos da aeronave.", exception);
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
            await _csv.DisposeAsync();
            await _frames.DisposeAsync();
        }
    }

    private static Dictionary<(byte Sender, byte Receiver, byte CommandSet, byte CommandId), CommandStats> LoadStats(string path)
    {
        var stats = new Dictionary<(byte, byte, byte, byte), CommandStats>();
        if (!File.Exists(path))
        {
            return stats;
        }

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var columns = line.Split(',');
            if (columns.Length != 13
                || !DateTimeOffset.TryParse(columns[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
                || !TryParseHex(columns[3], out var sender)
                || !TryParseHex(columns[4], out var receiver)
                || !TryParseHex(columns[7], out var commandSet)
                || !TryParseHex(columns[8], out var commandId)
                || !int.TryParse(columns[9], NumberStyles.None, CultureInfo.InvariantCulture, out var payloadLength))
            {
                continue;
            }

            var key = (sender, receiver, commandSet, commandId);
            if (!stats.TryGetValue(key, out var current))
            {
                current = new CommandStats();
                stats.Add(key, current);
            }
            current.Add(timestamp, payloadLength);
        }

        return stats;
    }

    private static string Hex(byte value) => $"0x{value:X2}";

    private static bool TryParseHex(string value, out byte result)
    {
        result = 0;
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    private sealed class CommandStats
    {
        public long Count { get; private set; }
        public DateTimeOffset FirstTimestamp { get; private set; }
        public DateTimeOffset LastTimestamp { get; private set; }
        public HashSet<int> PayloadLengths { get; } = [];

        public void Add(DateTimeOffset timestamp, int payloadLength)
        {
            if (Count == 0 || timestamp < FirstTimestamp) FirstTimestamp = timestamp;
            if (Count == 0 || timestamp > LastTimestamp) LastTimestamp = timestamp;
            Count++;
            PayloadLengths.Add(payloadLength);
        }
    }
}
