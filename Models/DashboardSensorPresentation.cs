using System.Text.RegularExpressions;

namespace DellR730xdFanControlCenter;

public enum DashboardIconKind
{
    Temperature,
    Fan,
    CpuUsage,
    MemoryUsage,
    IoUsage,
    SystemUsage,
    Power,
    Voltage,
    Current,
    Intrusion,
    FanRedundancy,
    PowerRedundancy,
    CmosBattery,
    RombBattery,
    UsbOverCurrent,
    PowerPolicy,
    StorageDrive,
    RaidController,
    StorageCache,
    GenericStatus,
}

public enum DashboardVisualState
{
    Normal,
    Information,
    Inactive,
    Unavailable,
    Warning,
    Critical,
}

public enum DashboardMotionKind
{
    None,
    FanRotation,
    LevelTransition,
    GaugeTransition,
    CurrentFlow,
    PowerActivity,
    WarningPulse,
}

public sealed record DashboardSensorPresentation(
    DashboardIconKind IconKind,
    DashboardVisualState VisualState,
    DashboardMotionKind MotionKind,
    double NormalizedLevel,
    double MotionPeriodSeconds,
    bool IsMotionActive,
    string AccentHex)
{
    private const double MaximumFanRpm = 18000;
    private const double SlowestFanRotationSeconds = 5.2;
    private const double FastestFanRotationSeconds = 0.11;
    private const double MinimumVoltageDisplay = 190;
    private const double MaximumVoltageDisplay = 260;

    private const string NormalAccentHex = "#FF22C55E";
    private const string InformationAccentHex = "#FF2563EB";
    private const string InactiveAccentHex = "#FF94A3B8";
    private const string UnavailableAccentHex = "#FFCBD5E1";
    private const string WarningAccentHex = "#FFFACC15";
    private const string CriticalAccentHex = "#FFEF4444";

    private static readonly IReadOnlyDictionary<string, DashboardIconKind> ExactKeyKinds =
        new Dictionary<string, DashboardIconKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["Temp"] = DashboardIconKind.Temperature,
            ["Inlet Temp"] = DashboardIconKind.Temperature,
            ["Exhaust Temp"] = DashboardIconKind.Temperature,
            ["CPU Usage"] = DashboardIconKind.CpuUsage,
            ["MEM Usage"] = DashboardIconKind.MemoryUsage,
            ["IO Usage"] = DashboardIconKind.IoUsage,
            ["SYS Usage"] = DashboardIconKind.SystemUsage,
            ["Pwr Consumption"] = DashboardIconKind.Power,
            ["Power Consumption"] = DashboardIconKind.Power,
            ["Intrusion"] = DashboardIconKind.Intrusion,
            ["Fan Redundancy"] = DashboardIconKind.FanRedundancy,
            ["PS Redundancy"] = DashboardIconKind.PowerRedundancy,
            ["Power Supply Redundancy"] = DashboardIconKind.PowerRedundancy,
            ["CMOS Battery"] = DashboardIconKind.CmosBattery,
            ["ROMB Battery"] = DashboardIconKind.RombBattery,
            ["BBU"] = DashboardIconKind.RombBattery,
            ["Backup Battery Unit"] = DashboardIconKind.RombBattery,
            ["Battery Backup Unit"] = DashboardIconKind.RombBattery,
            ["USB Over-current"] = DashboardIconKind.UsbOverCurrent,
            ["USB Over Current"] = DashboardIconKind.UsbOverCurrent,
            ["Power Optimized"] = DashboardIconKind.PowerPolicy,
        };

    private static readonly Regex IndexedTemperatureKey = new(
        @"^(?:Temp [0-9]+(?:\.[0-9]+)?|CPU[0-9]+ Temp)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexedFanKey = new(
        @"^Fan[0-9]+ RPM$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexedVoltageKey = new(
        @"^Voltage [0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexedCurrentKey = new(
        @"^Current [0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StorageDriveKey = new(
        @"^(?:Drive\s+[0-9]+|Physical\s+Disk\b|Virtual\s+Disk\b|Disk\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DashboardSensorPresentation FromSensor(SensorReading sensor)
    {
        ArgumentNullException.ThrowIfNull(sensor);

        var classification = ClassifyRawKey(sensor.Key, sensor.Unit);
        var iconKind = classification.IconKind;
        if (IsUnavailable(sensor))
        {
            return ForState(iconKind, DashboardVisualState.Unavailable);
        }

        if (sensor.NumericValue.HasValue &&
            !double.IsFinite(sensor.NumericValue.Value))
        {
            return ForState(iconKind, DashboardVisualState.Unavailable);
        }

        if (IsCritical(sensor, iconKind))
        {
            return ForSeverity(
                iconKind,
                classification.IsUnitOnlyElectrical,
                sensor.NumericValue,
                DashboardVisualState.Critical);
        }

        if (IsWarning(sensor))
        {
            return ForSeverity(
                iconKind,
                classification.IsUnitOnlyElectrical,
                sensor.NumericValue,
                DashboardVisualState.Warning);
        }

        if (ContainsWord(sensor.Status, "disabled") ||
            ContainsWord(sensor.Value, "disabled") ||
            ContainsWord(sensor.Status, "inactive") ||
            ContainsWord(sensor.Value, "inactive"))
        {
            return ForState(iconKind, DashboardVisualState.Inactive);
        }

        if (classification.IsUnitOnlyElectrical)
        {
            return sensor.NumericValue.HasValue
                ? ForUnitOnlyElectrical(iconKind)
                : ForState(iconKind, DashboardVisualState.Unavailable);
        }

        if (iconKind == DashboardIconKind.PowerPolicy &&
            (ContainsWord(sensor.Value, "oem") ||
             ContainsWord(sensor.Value, "dell") ||
             ContainsWord(sensor.Value, "custom") ||
             ContainsPhrase(sensor.Value, "vendor specific")))
        {
            return ForState(iconKind, DashboardVisualState.Information);
        }

        if (iconKind == DashboardIconKind.Fan &&
            (!sensor.NumericValue.HasValue || sensor.NumericValue.Value <= 0))
        {
            return ForState(iconKind, DashboardVisualState.Inactive);
        }

        if (iconKind == DashboardIconKind.GenericStatus)
        {
            return ForState(iconKind, DashboardVisualState.Normal);
        }

        if (!IsNumeric(iconKind))
        {
            return ForState(iconKind, DashboardVisualState.Normal);
        }

        if (!sensor.NumericValue.HasValue)
        {
            return ForState(iconKind, DashboardVisualState.Unavailable);
        }

        return ForNumeric(iconKind, sensor.NumericValue.Value);
    }

    private static DashboardSensorPresentation ForNumeric(DashboardIconKind iconKind, double value)
    {
        if (iconKind == DashboardIconKind.Fan && value <= 0)
        {
            return ForState(iconKind, DashboardVisualState.Inactive);
        }

        var style = GetNumericStyle(iconKind, value);
        var visualState = style.SemanticName switch
        {
            "Normal" => DashboardVisualState.Normal,
            "Warning" or "Caution" => DashboardVisualState.Warning,
            "Critical" => DashboardVisualState.Critical,
            _ => DashboardVisualState.Unavailable,
        };
        var motionKind = iconKind switch
        {
            DashboardIconKind.Fan => DashboardMotionKind.FanRotation,
            DashboardIconKind.Temperature or
            DashboardIconKind.CpuUsage or
            DashboardIconKind.MemoryUsage or
            DashboardIconKind.IoUsage or
            DashboardIconKind.SystemUsage => DashboardMotionKind.LevelTransition,
            DashboardIconKind.Voltage => DashboardMotionKind.GaugeTransition,
            DashboardIconKind.Current when value > 0 => DashboardMotionKind.CurrentFlow,
            DashboardIconKind.Power when value > 0 => DashboardMotionKind.PowerActivity,
            _ => DashboardMotionKind.None,
        };
        var motionPeriodSeconds = iconKind == DashboardIconKind.Fan
            ? CalculateFanRotationPeriod(value)
            : 0;

        return new DashboardSensorPresentation(
            iconKind,
            visualState,
            motionKind,
            NormalizeLevel(iconKind, value),
            motionPeriodSeconds,
            motionKind != DashboardMotionKind.None,
            style.ForegroundHex);
    }

    private static DashboardSensorPresentation ForState(
        DashboardIconKind iconKind,
        DashboardVisualState visualState,
        DashboardMotionKind motionKind = DashboardMotionKind.None)
    {
        var accentHex = visualState switch
        {
            DashboardVisualState.Normal => NormalAccentHex,
            DashboardVisualState.Information => InformationAccentHex,
            DashboardVisualState.Inactive => InactiveAccentHex,
            DashboardVisualState.Unavailable => UnavailableAccentHex,
            DashboardVisualState.Warning => WarningAccentHex,
            DashboardVisualState.Critical => CriticalAccentHex,
            _ => UnavailableAccentHex,
        };

        return new DashboardSensorPresentation(
            iconKind,
            visualState,
            motionKind,
            0,
            0,
            motionKind != DashboardMotionKind.None,
            accentHex);
    }

    private static DashboardSensorPresentation ForSeverity(
        DashboardIconKind iconKind,
        bool isUnitOnlyElectrical,
        double? numericValue,
        DashboardVisualState visualState)
    {
        if (!isUnitOnlyElectrical && IsNumeric(iconKind) && numericValue.HasValue)
        {
            var numericPresentation = ForNumeric(iconKind, numericValue.Value);
            var accentHex = visualState switch
            {
                DashboardVisualState.Warning => WarningAccentHex,
                DashboardVisualState.Critical => CriticalAccentHex,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(visualState),
                    visualState,
                    "Severity presentation requires warning or critical state."),
            };
            return numericPresentation with
            {
                VisualState = visualState,
                IsMotionActive = true,
                AccentHex = accentHex,
            };
        }

        return ForState(iconKind, visualState, DashboardMotionKind.WarningPulse);
    }

    private static DashboardSensorPresentation ForUnitOnlyElectrical(DashboardIconKind iconKind)
    {
        return new DashboardSensorPresentation(
            iconKind,
            DashboardVisualState.Information,
            DashboardMotionKind.None,
            0.5,
            0,
            false,
            InformationAccentHex);
    }

    private static (DashboardIconKind IconKind, bool IsUnitOnlyElectrical) ClassifyRawKey(string rawKey, string unit)
    {
        var key = rawKey.Trim();
        if (ExactKeyKinds.TryGetValue(key, out var exactKind))
        {
            return (exactKind, false);
        }

        if (IndexedTemperatureKey.IsMatch(key))
        {
            return (DashboardIconKind.Temperature, false);
        }

        if (IndexedFanKey.IsMatch(key))
        {
            return (DashboardIconKind.Fan, false);
        }

        if (IndexedVoltageKey.IsMatch(key))
        {
            return (DashboardIconKind.Voltage, false);
        }

        if (IndexedCurrentKey.IsMatch(key))
        {
            return (DashboardIconKind.Current, false);
        }

        if (key.Contains("cache", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.StorageCache, false);
        }

        if (key.Contains("PERC", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("RAID", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Storage Controller", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.RaidController, false);
        }

        if (StorageDriveKey.IsMatch(key))
        {
            return (DashboardIconKind.StorageDrive, false);
        }

        if (unit.Contains("degrees C", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.Temperature, false);
        }

        if (unit.Contains("RPM", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.Fan, false);
        }

        if (unit.Contains("Watts", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.Power, true);
        }

        if (unit.Contains("Volts", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.Voltage, true);
        }

        if (unit.Contains("Amps", StringComparison.OrdinalIgnoreCase))
        {
            return (DashboardIconKind.Current, true);
        }

        return (DashboardIconKind.GenericStatus, false);
    }

    private static bool IsNumeric(DashboardIconKind iconKind)
    {
        return iconKind is DashboardIconKind.Temperature or
            DashboardIconKind.Fan or
            DashboardIconKind.CpuUsage or
            DashboardIconKind.MemoryUsage or
            DashboardIconKind.IoUsage or
            DashboardIconKind.SystemUsage or
            DashboardIconKind.Power or
            DashboardIconKind.Voltage or
            DashboardIconKind.Current;
    }

    private static bool IsUnavailable(SensorReading sensor)
    {
        var status = sensor.Status.Trim();
        if (status.Equals("ns", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("na", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasActionableStatus = status.Equals("nc", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("lnc", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("unc", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("warning", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("lc", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("lcr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("lnr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("cr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("nr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("uc", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("ucr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("unr", StringComparison.OrdinalIgnoreCase) ||
                                  status.Equals("critical", StringComparison.OrdinalIgnoreCase);
        return !hasActionableStatus &&
               (sensor.Value.Trim().Equals("No Reading", StringComparison.OrdinalIgnoreCase) ||
                sensor.Value.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCritical(SensorReading sensor, DashboardIconKind iconKind)
    {
        var status = sensor.Status.Trim();
        if (status.Equals("lc", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("lcr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("lnr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("cr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("nr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("uc", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("ucr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("unr", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("critical", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ContainsWord(sensor.Status, "failure") ||
            ContainsWord(sensor.Status, "failed") ||
            ContainsWord(sensor.Status, "fault") ||
            ContainsWord(sensor.Status, "lost") ||
            ContainsWord(sensor.Value, "failure") ||
            ContainsWord(sensor.Value, "failed") ||
            ContainsWord(sensor.Value, "fault") ||
            ContainsWord(sensor.Value, "lost"))
        {
            return true;
        }

        return (iconKind is DashboardIconKind.UsbOverCurrent or DashboardIconKind.Intrusion) &&
               (ContainsWord(sensor.Value, "asserted") || ContainsWord(sensor.Status, "asserted"));
    }

    private static bool IsWarning(SensorReading sensor)
    {
        var status = sensor.Status.Trim();
        return status.Equals("lnc", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("unc", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("nc", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("warning", StringComparison.OrdinalIgnoreCase) ||
               ContainsWord(sensor.Status, "degraded") ||
               ContainsWord(sensor.Value, "degraded") ||
               ContainsPhrase(sensor.Status, "not redundant") ||
               ContainsPhrase(sensor.Value, "not redundant");
    }

    private static HeroMetricSeverityStyle GetNumericStyle(DashboardIconKind iconKind, double value)
    {
        return iconKind switch
        {
            DashboardIconKind.Temperature => HeroMetricSeverityStyle.ForTemperature(value),
            DashboardIconKind.Fan => HeroMetricSeverityStyle.ForFanRpm(value),
            DashboardIconKind.Power => HeroMetricSeverityStyle.ForPowerWatts(value),
            DashboardIconKind.Voltage => HeroMetricSeverityStyle.ForVoltage(value),
            DashboardIconKind.Current => HeroMetricSeverityStyle.ForCurrentAmps(value),
            DashboardIconKind.CpuUsage or
            DashboardIconKind.MemoryUsage or
            DashboardIconKind.IoUsage or
            DashboardIconKind.SystemUsage => value switch
            {
                < 70 => new HeroMetricSeverityStyle("Normal", NormalAccentHex),
                < 90 => new HeroMetricSeverityStyle("Warning", WarningAccentHex),
                _ => new HeroMetricSeverityStyle("Critical", CriticalAccentHex),
            },
            _ => new HeroMetricSeverityStyle("Unknown", UnavailableAccentHex),
        };
    }

    private static double NormalizeLevel(DashboardIconKind iconKind, double value)
    {
        return iconKind switch
        {
            DashboardIconKind.Temperature or
            DashboardIconKind.CpuUsage or
            DashboardIconKind.MemoryUsage or
            DashboardIconKind.IoUsage or
            DashboardIconKind.SystemUsage => Math.Clamp(value / 100, 0, 1),
            DashboardIconKind.Fan => Math.Clamp(value / MaximumFanRpm, 0, 1),
            DashboardIconKind.Voltage => Math.Clamp(
                (value - MinimumVoltageDisplay) / (MaximumVoltageDisplay - MinimumVoltageDisplay),
                0,
                1),
            _ => 0,
        };
    }

    private static double CalculateFanRotationPeriod(double rpm)
    {
        if (rpm >= MaximumFanRpm)
        {
            return FastestFanRotationSeconds;
        }

        var normalizedRpm = Math.Clamp(rpm / MaximumFanRpm, 0, 1);
        var slowestRotationsPerSecond = 1 / SlowestFanRotationSeconds;
        var fastestRotationsPerSecond = 1 / FastestFanRotationSeconds;
        var rotationsPerSecond = slowestRotationsPerSecond +
                                 (normalizedRpm * (fastestRotationsPerSecond - slowestRotationsPerSecond));
        return 1 / rotationsPerSecond;
    }

    private static bool ContainsWord(string source, string word)
    {
        return Regex.IsMatch(
            source,
            $@"(?:^|[^A-Za-z0-9]){Regex.Escape(word)}(?:$|[^A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsPhrase(string source, string phrase)
    {
        return source.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }
}
