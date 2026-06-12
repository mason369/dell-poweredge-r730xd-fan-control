namespace DellR730xdFanControlCenter;

public static class SensorSubtitleFormatter
{
    public static string Format(SensorReading sensor)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sensor.SensorId))
        {
            parts.Add(LocalizationService.Format("SensorSubtitle.SdrRecord", FormatRecordId(sensor.SensorId)));
        }

        if (!string.IsNullOrWhiteSpace(sensor.Entity))
        {
            parts.Add(LocalizationService.Format("SensorSubtitle.Entity", sensor.Entity.Trim()));
        }

        return parts.Count == 0
            ? "SDR"
            : string.Join(" / ", parts);
    }

    public static string FormatRecordId(string recordId)
    {
        var trimmed = recordId.Trim();
        return trimmed.Length > 1 && trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase)
            ? $"0x{trimmed[..^1]}"
            : trimmed;
    }
}
