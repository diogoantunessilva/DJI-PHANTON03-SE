using System.Buffers.Binary;

namespace Phanton3.TelemetryProbe;

internal sealed class DumlFrame
{
    public const int MinimumLength = 13;
    public const int MaximumLength = 1023;

    private DumlFrame(byte[] bytes)
    {
        Bytes = bytes;
        TotalLength = bytes.Length;
        ProtocolVersion = bytes[2] >> 2;
        HeaderCrc8 = bytes[3];
        Sender = bytes[4];
        Receiver = bytes[5];
        Sequence = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
        Flags = bytes[8];
        CommandSet = bytes[9];
        CommandId = bytes[10];
        PayloadLength = bytes.Length - MinimumLength;
        PayloadHex = Convert.ToHexString(bytes.AsSpan(11, PayloadLength));
        ReceivedCrc16 = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2, 2));
        Crc8Ok = DjiCrc.Crc8(bytes.AsSpan(0, 3)) == HeaderCrc8;
        Crc16Ok = DjiCrc.Crc16(bytes.AsSpan(0, bytes.Length - 2)) == ReceivedCrc16;
    }

    public byte[] Bytes { get; }
    public int TotalLength { get; }
    public int ProtocolVersion { get; }
    public byte HeaderCrc8 { get; }
    public byte Sender { get; }
    public byte Receiver { get; }
    public ushort Sequence { get; }
    public byte Flags { get; }
    public byte CommandSet { get; }
    public byte CommandId { get; }
    public int PayloadLength { get; }
    public string PayloadHex { get; }
    public ReadOnlySpan<byte> Payload => Bytes.AsSpan(11, PayloadLength);
    public ushort ReceivedCrc16 { get; }
    public bool Crc8Ok { get; }
    public bool Crc16Ok { get; }

    public static DumlFrame Parse(byte[] bytes)
    {
        if (bytes.Length < MinimumLength || bytes.Length > MaximumLength || bytes[0] != 0x55)
        {
            throw new ArgumentException("Quadro DUML com SOF ou tamanho inválido.", nameof(bytes));
        }

        var declaredLength = bytes[1] | ((bytes[2] & 0x03) << 8);
        if (declaredLength != bytes.Length)
        {
            throw new ArgumentException("Tamanho declarado diferente do quadro recebido.", nameof(bytes));
        }

        return new DumlFrame(bytes);
    }
}
