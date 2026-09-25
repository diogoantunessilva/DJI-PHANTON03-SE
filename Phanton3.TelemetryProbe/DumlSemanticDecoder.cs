using System.Buffers.Binary;

namespace Phanton3.TelemetryProbe;

internal static class DumlSemanticDecoder
{
    public static DumlSemantic? Decode(DumlFrame frame)
    {
        // Um CRC inválido não deve produzir telemetria interpretada.
        if (frame.ProtocolVersion != 1 || !frame.Crc8Ok || !frame.Crc16Ok)
        {
            return null;
        }

        var payload = frame.Payload;
        if (frame.CommandSet == 0x06 && frame.CommandId == 0x05 && payload.Length == 13)
        {
            var channels = new RcChannels(
                Aileron: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2)),
                Elevator: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
                Throttle: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
                Rudder: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)),
                GyroValue: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)),
                WheelInfo: payload[10],
                Status1: payload[11],
                Status2: payload[12]);

            var display = $"RC Channels: Aileron={channels.Aileron} Elevator={channels.Elevator} Throttle={channels.Throttle} Rudder={channels.Rudder} GyroValue={channels.GyroValue} WheelInfo={channels.WheelInfo} Status1=0x{channels.Status1:X2} Status2=0x{channels.Status2:X2} GoHomePressed={Bool(channels.GoHomePressed)} ModeBits={channels.ModeBits} Custom2={Bool(channels.Custom2)} Custom1={Bool(channels.Custom1)} Playback={Bool(channels.Playback)} Shutter={Bool(channels.Shutter)} Record={Bool(channels.Record)}";
            return new DumlSemantic(display, channels);
        }

        if (frame.CommandSet == 0x07 && frame.CommandId == 0x09 && payload.Length == 1)
        {
            return new DumlSemantic($"WiFi RSSI Raw = {payload[0]}");
        }

        if (frame.CommandSet == 0x07 && frame.CommandId == 0x12 && payload.Length == 1)
        {
            return new DumlSemantic($"WiFi Signal Status Raw = {payload[0]}");
        }

        if (frame.CommandSet == 0x06 && frame.CommandId == 0x1E)
        {
            return new DumlSemantic($"RC Battery Info | payload bruto={frame.PayloadHex}");
        }

        if (frame.CommandSet == 0x00 && frame.CommandId == 0x0E)
        {
            return new DumlSemantic("Heartbeat");
        }

        return null;
    }

    private static string Bool(bool value) => value ? "true" : "false";
}

internal sealed record DumlSemantic(string DisplayText, RcChannels? Channels = null);

internal sealed record RcChannels(
    ushort Aileron,
    ushort Elevator,
    ushort Throttle,
    ushort Rudder,
    ushort GyroValue,
    byte WheelInfo,
    byte Status1,
    byte Status2)
{
    public bool GoHomePressed => (Status1 & 0x08) != 0;
    public byte ModeBits => (byte)((Status1 >> 4) & 0x03);
    public bool Custom2 => (Status2 & 0x08) != 0;
    public bool Custom1 => (Status2 & 0x10) != 0;
    public bool Playback => (Status2 & 0x20) != 0;
    public bool Shutter => (Status2 & 0x40) != 0;
    public bool Record => (Status2 & 0x80) != 0;
}
