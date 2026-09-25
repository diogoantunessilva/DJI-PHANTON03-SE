namespace Phanton3.TelemetryProbe;

// Polinômios refletidos usados pelo DUML v1: CRC8/Dallas e CRC16/Kermit.
internal static class DjiCrc
{
    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        var crc = 0x77;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x8C : 0);
            }
        }

        return (byte)crc;
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        var crc = 0x3692;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x8408 : 0);
            }
        }

        return (ushort)crc;
    }
}
