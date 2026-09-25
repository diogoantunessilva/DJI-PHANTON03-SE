namespace Phanton3.TelemetryProbe;

internal enum SourceSelection
{
    All,
    Controller,
    Aircraft
}

internal sealed record CaptureOptions(SourceSelection Source, bool RcTestMode)
{
    public static CaptureOptions Parse(string[] args)
    {
        var source = SourceSelection.All;
        var sourceSpecified = false;
        var rcTestMode = false;

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--source", StringComparison.OrdinalIgnoreCase))
            {
                if (sourceSpecified || ++index >= args.Length)
                {
                    throw new ArgumentException("Use --source controller, --source aircraft ou --source all uma única vez.");
                }

                source = args[index].ToLowerInvariant() switch
                {
                    "controller" => SourceSelection.Controller,
                    "aircraft" => SourceSelection.Aircraft,
                    "all" => SourceSelection.All,
                    _ => throw new ArgumentException("Fonte inválida. Use controller, aircraft ou all.")
                };
                sourceSpecified = true;
            }
            else if (string.Equals(args[index], "--rc-test", StringComparison.OrdinalIgnoreCase))
            {
                rcTestMode = true;
            }
            else
            {
                throw new ArgumentException($"Argumento desconhecido: {args[index]}. Use --source controller|aircraft|all e opcionalmente --rc-test.");
            }
        }

        if (rcTestMode && source == SourceSelection.Aircraft)
        {
            throw new ArgumentException("--rc-test exige a fonte controller ou all.");
        }

        return new CaptureOptions(source, rcTestMode);
    }
}
