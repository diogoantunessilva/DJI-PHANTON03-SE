using Phanton3.TelemetryProbe;

var sample = Convert.FromHexString("551A04B10E02B32500060500040004000400040004002004E0E6");

var frame = DumlFrame.Parse(sample);
Check(frame.TotalLength == 26 && frame.ProtocolVersion == 1, "comprimento e versão");
Check(frame.HeaderCrc8 == 0xB1 && frame.Sender == 0x0E && frame.Receiver == 0x02, "cabeçalho");
Check(frame.Sequence == 0x25B3 && frame.Flags == 0 && frame.CommandSet == 6 && frame.CommandId == 5, "campos DUML");
Check(frame.PayloadLength == 13 && frame.ReceivedCrc16 == 0xE6E0, "payload e CRC16 recebido");
Check(frame.Crc8Ok && frame.Crc16Ok, "CRCs do quadro real");
var baselineChannels = DumlSemanticDecoder.Decode(frame)?.Channels
    ?? throw new InvalidOperationException("Falha na verificação: canais RC reconhecidos");
Check(baselineChannels.Aileron == 1024 && baselineChannels.Elevator == 1024
    && baselineChannels.Throttle == 1024 && baselineChannels.Rudder == 1024
    && baselineChannels.GyroValue == 1024, "UInt16 little-endian sem escala");
Check(baselineChannels.WheelInfo == 0 && baselineChannels.Status1 == 0x20
    && baselineChannels.Status2 == 0x04 && baselineChannels.ModeBits == 2, "status bruto e modo");

var flagsFrameBytes = (byte[])sample.Clone();
flagsFrameBytes[22] = 0x38;
flagsFrameBytes[23] = 0xF8;
var flagsCrc = DjiCrc.Crc16(flagsFrameBytes.AsSpan(0, flagsFrameBytes.Length - 2));
flagsFrameBytes[^2] = (byte)flagsCrc;
flagsFrameBytes[^1] = (byte)(flagsCrc >> 8);
var flagsChannels = DumlSemanticDecoder.Decode(DumlFrame.Parse(flagsFrameBytes))?.Channels;
Check(flagsChannels is not null && flagsChannels.GoHomePressed && flagsChannels.ModeBits == 3
    && flagsChannels.Custom2 && flagsChannels.Custom1 && flagsChannels.Playback
    && flagsChannels.Shutter && flagsChannels.Record, "bits de botões e modo");

Check(DumlSemanticDecoder.Decode(DumlFrame.Parse(Convert.FromHexString("550E04661B02E70C00070964E60D")))?.DisplayText == "WiFi RSSI Raw = 100", "RSSI bruto");
Check(DumlSemanticDecoder.Decode(DumlFrame.Parse(Convert.FromHexString("550E04661B02E80C000712007464")))?.DisplayText == "WiFi Signal Status Raw = 0", "sinal Wi-Fi bruto");
Check(DumlSemanticDecoder.Decode(DumlFrame.Parse(Convert.FromHexString("551204C70E02BF2500061E400E0000375170")))?.DisplayText.StartsWith("RC Battery Info | payload bruto=") == true, "bateria sem layout presumido");
Check(DumlSemanticDecoder.Decode(DumlFrame.Parse(Convert.FromHexString("550D04331B02170240000E1D5D")))?.DisplayText == "Heartbeat", "heartbeat identificado");

var fragmented = new DumlStreamParser();
Check(fragmented.Feed(sample.AsSpan(0, 1)).Frames.Count == 0, "SOF isolado");
Check(fragmented.Feed(sample.AsSpan(1, 6)).Frames.Count == 0, "quadro parcial");
Check(fragmented.Feed(sample.AsSpan(7)).Frames.Count == 1, "quadro remontado");
Check(fragmented.PendingByteCount == 0, "buffer esvaziado");

var concatenated = new DumlStreamParser();
var joined = sample.Concat(sample).ToArray();
Check(concatenated.Feed(joined).Frames.Count == 2, "dois quadros em uma leitura");

var badCrc8 = (byte[])sample.Clone();
badCrc8[3] ^= 1;
var replacementCrc16 = DjiCrc.Crc16(badCrc8.AsSpan(0, badCrc8.Length - 2));
badCrc8[^2] = (byte)replacementCrc16;
badCrc8[^1] = (byte)(replacementCrc16 >> 8);
var badCrc16 = (byte[])sample.Clone();
badCrc16[^1] ^= 1;
var corrupt = new DumlStreamParser().Feed(badCrc8.Concat(badCrc16).ToArray()).Frames;
Check(corrupt.Count == 2, "quadros inválidos preservados");
Check(!corrupt[0].Crc8Ok && corrupt[0].Crc16Ok, "CRC8 inválido sinalizado");
Check(corrupt[1].Crc8Ok && !corrupt[1].Crc16Ok, "CRC16 inválido sinalizado");
Check(DumlSemanticDecoder.Decode(corrupt[0]) is null && DumlSemanticDecoder.Decode(corrupt[1]) is null, "sem semântica para CRC inválido");

var resync = new DumlStreamParser().Feed(new byte[] { 0x00, 0x55, 0x01, 0x04 }.Concat(sample).ToArray());
Check(resync.InvalidLengths == 1 && resync.Frames.Count == 1, "ressincronização após tamanho inválido");

var testDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Phanton3-DumlChecks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testDirectory);
var source = new TelemetrySource("checks", "192.168.1.1", 2345, "Wi-Fi",
    Path.Combine(testDirectory, "raw.bin"), Path.Combine(testDirectory, "telemetry.log"),
    Path.Combine(testDirectory, "duml_frames.bin"), Path.Combine(testDirectory, "duml_frames.csv"),
    Path.Combine(testDirectory, "commands_summary.csv"), Path.Combine(testDirectory, "rc_channels.csv"));

try
{
    await using (var sink = await DumlFrameSink.OpenAsync(source))
    {
        await sink.WriteFrameAsync(frame, DateTimeOffset.Now);
        await sink.WriteFrameAsync(corrupt[1], DateTimeOffset.Now);
    }

    Check(new FileInfo(source.FramesCapturePath).Length == 2 * sample.Length, "arquivo de quadros binários");
    var csvRows = File.ReadAllLines(source.FramesCsvPath);
    Check(csvRows.Length == 3 && csvRows[1].EndsWith("true,true") && csvRows[2].EndsWith("true,false"), "CSV de quadros e CRC inválido");
    var rcRows = File.ReadAllLines(source.RcChannelsCsvPath);
    Check(rcRows.Length == 2 && rcRows[0] == "timestamp,aileron,elevator,throttle,rudder,gyro,wheel,status1,status2"
        && rcRows[1].EndsWith(",1024,1024,1024,1024,1024,0,32,4"), "CSV de canais RC brutos");
    Check(File.ReadAllLines(source.CommandsSummaryPath).Single(line => line.StartsWith("0x0E,0x02,0x06,0x05,")).EndsWith(",2"), "resumo inicial");

    await using (var sink = await DumlFrameSink.OpenAsync(source))
    {
        await sink.WriteFrameAsync(frame, DateTimeOffset.Now);
    }
    Check(File.ReadAllLines(source.CommandsSummaryPath).Single(line => line.StartsWith("0x0E,0x02,0x06,0x05,")).EndsWith(",3"), "resumo acumulado após reinício");
    Check(File.ReadAllLines(source.RcChannelsCsvPath).Length == 3, "CSV RC acumulado após reinício");
}
finally
{
    if (!testDirectory.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diretório de teste fora da pasta temporária.");
    }
    Directory.Delete(testDirectory, recursive: true);
}

var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "Phanton3.TelemetryProbe", "captures", "controller_2345.bin"));
if (File.Exists(capturePath))
{
    using var capture = new FileStream(capturePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var snapshot = new byte[capture.Length];
    capture.ReadExactly(snapshot);

    var replay = new DumlStreamParser();
    var count = 0;
    var rcCount = 0;
    for (var offset = 0; offset < snapshot.Length;)
    {
        var length = Math.Min(1 + (offset % 47), snapshot.Length - offset);
        foreach (var parsed in replay.Feed(snapshot.AsSpan(offset, length)).Frames)
        {
            Check(parsed.Crc8Ok && parsed.Crc16Ok, "CRC na captura real");
            if (DumlSemanticDecoder.Decode(parsed)?.Channels is not null)
            {
                rcCount++;
            }
            count++;
        }
        offset += length;
    }
    Check(count > 0 && rcCount > 0, "captura real e canais RC reconhecidos");
    Console.WriteLine($"Captura real: {count} quadros válidos, {rcCount} quadros de canais RC.");
}

Console.WriteLine("Verificações DUML concluídas.");

static void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Falha na verificação: {description}");
    }
}
