namespace DellR730xdFanControlCenter;

public sealed class SensorReading
{
    public string Key { get; set; } = string.Empty;

    public string SensorId { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public double? NumericValue { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();
        AddIfPresent(Key);
        AddIfPresent(SensorId);
        AddIfPresent(Entity);
        AddIfPresent(Value);
        AddIfPresent(Unit);
        AddIfPresent(Status);
        return string.Join(" / ", parts);

        void AddIfPresent(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }
    }
}
