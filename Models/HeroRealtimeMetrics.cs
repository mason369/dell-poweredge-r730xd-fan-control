using System;
using System.Collections.Generic;
using System.Linq;

namespace DellR730xdFanControlCenter;

public sealed class HeroRealtimeMetrics
{
    public double? CurrentTemperatureCelsius { get; init; }

    public double? AverageFanRpm { get; init; }

    public double? PowerWatts { get; init; }

    public double? AverageVoltage { get; init; }

    public double? TotalCurrent { get; init; }

    public IReadOnlyList<HeroRealtimeMetricItem> TemperatureItems { get; init; } = [];

    public IReadOnlyList<HeroRealtimeMetricItem> FanItems { get; init; } = [];

    public IReadOnlyList<HeroRealtimeMetricItem> PowerItems { get; init; } = [];

    public IReadOnlyList<HeroRealtimeMetricItem> VoltageItems { get; init; } = [];

    public IReadOnlyList<HeroRealtimeMetricItem> CurrentItems { get; init; } = [];

    public int HiddenTemperatureItemCount { get; init; }

    public int HiddenFanItemCount { get; init; }

    public int HiddenPowerItemCount { get; init; }

    public int HiddenVoltageItemCount { get; init; }

    public int HiddenCurrentItemCount { get; init; }

    public static HeroRealtimeMetrics FromSensors(IEnumerable<SensorReading> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        var rows = sensors.ToList();
        var temperatureRows = rows
            .Where(IsTemperatureSensor)
            .ToList();
        var fanRows = rows
            .Where(IsFanSensor)
            .ToList();
        var powerRows = rows
            .Where(IsPowerWattsSensor)
            .ToList();
        var voltageRows = rows
            .Where(IsVoltageSensor)
            .ToList();
        var currentRows = rows
            .Where(IsCurrentSensor)
            .ToList();

        return new HeroRealtimeMetrics
        {
            CurrentTemperatureCelsius = AverageOrNull(temperatureRows, 1),
            AverageFanRpm = AverageOrNull(fanRows, 0),
            PowerWatts = powerRows.FirstOrDefault()?.NumericValue is { } watts ? Math.Round(watts, 1) : null,
            AverageVoltage = AverageOrNull(voltageRows, 1),
            TotalCurrent = currentRows.Count == 0 ? null : Math.Round(currentRows.Sum(sensor => sensor.NumericValue!.Value), 2),
            TemperatureItems = BuildItems(temperatureRows),
            FanItems = BuildItems(fanRows),
            PowerItems = BuildItems(powerRows),
            VoltageItems = BuildItems(voltageRows),
            CurrentItems = BuildItems(currentRows),
            HiddenTemperatureItemCount = HiddenCount(temperatureRows),
            HiddenFanItemCount = HiddenCount(fanRows),
            HiddenPowerItemCount = HiddenCount(powerRows),
            HiddenVoltageItemCount = HiddenCount(voltageRows),
            HiddenCurrentItemCount = HiddenCount(currentRows),
        };
    }

    private static double? AverageOrNull(IReadOnlyCollection<SensorReading> sensors, int digits)
    {
        return sensors.Count == 0
            ? null
            : Math.Round(sensors.Average(sensor => sensor.NumericValue!.Value), digits);
    }

    private static IReadOnlyList<HeroRealtimeMetricItem> BuildItems(IEnumerable<SensorReading> sensors)
    {
        return sensors
            .Select(sensor => new HeroRealtimeMetricItem(
                sensor.Key,
                Math.Round(sensor.NumericValue!.Value, 1),
                sensor.Unit))
            .ToList();
    }

    private static int HiddenCount(IReadOnlyCollection<SensorReading> sensors)
    {
        return 0;
    }

    private static bool IsTemperatureSensor(SensorReading sensor)
    {
        return sensor.NumericValue.HasValue &&
               (sensor.Unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase) ||
                sensor.Key.Contains("Temp", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFanSensor(SensorReading sensor)
    {
        return sensor.NumericValue.HasValue &&
               sensor.Key.StartsWith("Fan", StringComparison.OrdinalIgnoreCase) &&
               sensor.Unit.Contains("RPM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerWattsSensor(SensorReading sensor)
    {
        return sensor.NumericValue.HasValue &&
               (sensor.Unit.Contains("Watts", StringComparison.OrdinalIgnoreCase) ||
                sensor.Key.Contains("Pwr Consumption", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsVoltageSensor(SensorReading sensor)
    {
        return sensor.NumericValue.HasValue &&
               sensor.Unit.Contains("Volts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurrentSensor(SensorReading sensor)
    {
        return sensor.NumericValue.HasValue &&
               sensor.Unit.Contains("Amps", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record HeroRealtimeMetricItem(string Key, double Value, string Unit);
