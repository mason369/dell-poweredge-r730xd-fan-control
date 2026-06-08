namespace DellR730xdFanControlCenter;

public sealed record HeroMetricSeverityStyle(string SemanticName, string ForegroundHex)
{
    public static HeroMetricSeverityStyle ForTemperature(double? celsius)
    {
        if (!celsius.HasValue)
        {
            return Unknown;
        }

        return celsius.Value switch
        {
            < 60 => Normal,
            < 70 => Warning,
            < 80 => Caution,
            _ => Critical,
        };
    }

    public static HeroMetricSeverityStyle ForFanRpm(double? rpm)
    {
        if (!rpm.HasValue)
        {
            return Unknown;
        }

        return rpm.Value switch
        {
            < 500 => Critical,
            < 1500 => Caution,
            < 2500 => Warning,
            <= 6000 => Normal,
            <= 9000 => Warning,
            _ => Caution,
        };
    }

    public static HeroMetricSeverityStyle ForPowerWatts(double? watts)
    {
        if (!watts.HasValue)
        {
            return Unknown;
        }

        return watts.Value switch
        {
            < 500 => Normal,
            < 700 => Warning,
            < 900 => Caution,
            _ => Critical,
        };
    }

    public static HeroMetricSeverityStyle ForVoltage(double? volts)
    {
        if (!volts.HasValue)
        {
            return Unknown;
        }

        return volts.Value switch
        {
            >= 210 and <= 240 => Normal,
            >= 200 and < 210 => Warning,
            > 240 and <= 250 => Warning,
            >= 190 and < 200 => Caution,
            > 250 and <= 260 => Caution,
            _ => Critical,
        };
    }

    public static HeroMetricSeverityStyle ForCurrentAmps(double? amps)
    {
        if (!amps.HasValue)
        {
            return Unknown;
        }

        return amps.Value switch
        {
            < 4 => Normal,
            < 6 => Warning,
            < 8 => Caution,
            _ => Critical,
        };
    }

    private static readonly HeroMetricSeverityStyle Unknown = new("Unknown", "#FFCBD5E1");
    private static readonly HeroMetricSeverityStyle Normal = new("Normal", "#FF22C55E");
    private static readonly HeroMetricSeverityStyle Warning = new("Warning", "#FFFACC15");
    private static readonly HeroMetricSeverityStyle Caution = new("Caution", "#FFF97316");
    private static readonly HeroMetricSeverityStyle Critical = new("Critical", "#FFEF4444");
}
