using System.Globalization;
using System.Text;

namespace Phanton3.TelemetryProbe;

internal static class RcChannelDisplay
{
    // Faixa observada no teste controlado; a porcentagem é apenas uma referência visual.
    private const int Minimum = 364;
    private const int Center = 1024;
    private const int Maximum = 1684;
    private const int BarWidth = 19;

    public static string Format(RcChannels channels)
    {
        var lines = new StringBuilder();
        AppendChannel(lines, "AILERON", channels.Aileron);
        AppendChannel(lines, "ELEVATOR", channels.Elevator);
        AppendChannel(lines, "THROTTLE", channels.Throttle);
        AppendChannel(lines, "RUDDER", channels.Rudder);
        AppendChannel(lines, "GYRO", channels.GyroValue);
        lines.Append("WheelInfo=").Append(channels.WheelInfo)
            .Append(" | Status1=0x").Append(channels.Status1.ToString("X2", CultureInfo.InvariantCulture))
            .Append(" | Status2=0x").Append(channels.Status2.ToString("X2", CultureInfo.InvariantCulture))
            .Append(" | GoHomePressed=").Append(channels.GoHomePressed)
            .Append(" | ModeBits=").Append(channels.ModeBits)
            .Append(" | Custom2=").Append(channels.Custom2)
            .Append(" | Custom1=").Append(channels.Custom1)
            .Append(" | Playback=").Append(channels.Playback)
            .Append(" | Shutter=").Append(channels.Shutter)
            .Append(" | Record=").Append(channels.Record);
        return lines.ToString();
    }

    public static double Normalize(ushort raw)
    {
        var normalized = raw >= Center
            ? (raw - Center) * 100.0 / (Maximum - Center)
            : (raw - Center) * 100.0 / (Center - Minimum);
        return Math.Clamp(normalized, -100.0, 100.0);
    }

    public static string FormatChannel(string name, ushort raw)
    {
        var normalized = Normalize(raw);
        var filled = (int)Math.Round((normalized + 100.0) * BarWidth / 200.0,
            MidpointRounding.AwayFromZero);
        var percentage = normalized.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        return $"{name,-8} Raw: {raw,4} | Normalized: {percentage}% | {new string('█', filled)}{new string('░', BarWidth - filled)}";
    }

    private static void AppendChannel(StringBuilder lines, string name, ushort raw)
    {
        lines.AppendLine(FormatChannel(name, raw));
    }
}
