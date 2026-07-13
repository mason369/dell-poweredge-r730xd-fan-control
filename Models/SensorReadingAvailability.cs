namespace DellR730xdFanControlCenter;

public static class SensorReadingAvailability
{
    private static readonly HashSet<string> UnavailableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ns",
        "na",
    };

    private static readonly HashSet<string> ActionableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "nc",
        "lnc",
        "unc",
        "warning",
        "lc",
        "lcr",
        "lnr",
        "cr",
        "nr",
        "uc",
        "ucr",
        "unr",
        "critical",
    };

    private static readonly HashSet<string> UnavailableValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "No Reading",
        "Disabled",
        "Not Available",
        "N/A",
        "NA",
        "Unknown",
    };

    public static bool IsDisplayable(SensorReading sensor)
    {
        ArgumentNullException.ThrowIfNull(sensor);

        var key = sensor.Key.Trim();
        var status = sensor.Status.Trim();
        var value = sensor.Value.Trim();
        if (key.Length == 0 || UnavailableStatuses.Contains(status))
        {
            return false;
        }

        if (IsActionable(status, value))
        {
            return true;
        }

        if (sensor.NumericValue.HasValue && !double.IsFinite(sensor.NumericValue.Value))
        {
            return false;
        }

        if (UnavailableValues.Contains(value))
        {
            return false;
        }

        return sensor.NumericValue.HasValue || status.Length > 0 || value.Length > 0;
    }

    private static bool IsActionable(string status, string value)
    {
        if (ActionableStatuses.Contains(status))
        {
            return true;
        }

        return ContainsActionableWord(status) || ContainsActionableWord(value);
    }

    private static bool ContainsActionableWord(string text)
    {
        return text.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("lost", StringComparison.OrdinalIgnoreCase);
    }
}
