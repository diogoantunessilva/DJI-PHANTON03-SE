using System.Globalization;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal sealed class AircraftFrameSink : IFrameSink
{
    private const string FramesHeader = "timestamp,source,length,sender,receiver,sequence,flags,cmdSet,cmdId,payloadLength,payloadHex,crc8Ok,crc16Ok";
    private const string SummaryHeader = "src,dst,cmdSet,cmdId,payloadLen,count,firstTimestamp,lastTimestamp,frequencyHz";
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TelemetrySource _source;
    private readonly FileStream _frames;
    private readonly StreamWriter _csv;
    private readonly bool _showFrames;
    private readonly Dictionary<(byte Sender, byte Receiver, byte CommandSet, byte CommandId, int PayloadLength), CommandStats> _stats;
    private DateTimeOffset _lastSummaryWrite = DateTimeOffset.MinValue;
    private bool _summaryDirty = true;

    private AircraftFrameSink(TelemetrySource source, bool showFrames)
    {
        _source = source;
        _showFrames = showFrames;
        _stats = new();
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

            var key = (frame.Sender, frame.Receiver, frame.CommandSet, frame.CommandId, frame.PayloadLength);
            if (!_stats.TryGetValue(key, out var stats))
            {
                stats = new CommandStats();
                _stats.Add(key, stats);
            }
            stats.Add(timestamp);
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
                     .ThenBy(item => item.Key.CommandId)
                     .ThenBy(item => item.Key.PayloadLength))
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
                .Append(entry.Key.PayloadLength.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(stats.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(stats.FirstTimestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(stats.LastTimestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(frequency.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine();
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

    private static string Hex(byte value) => $"0x{value:X2}";

    private sealed class CommandStats
    {
        public long Count { get; private set; }
        public DateTimeOffset FirstTimestamp { get; private set; }
        public DateTimeOffset LastTimestamp { get; private set; }
        public void Add(DateTimeOffset timestamp)
        {
            if (Count == 0 || timestamp < FirstTimestamp) FirstTimestamp = timestamp;
            if (Count == 0 || timestamp > LastTimestamp) LastTimestamp = timestamp;
            Count++;
        }
    }
}
