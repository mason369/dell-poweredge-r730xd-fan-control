using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace DellR730xdFanControlCenter;

public sealed class FanPreset
{
    public const string ManualKind = "Manual";
    public const string RestoreManualKind = "RestoreManual";
    public const string DellAutoKind = "DellAuto";
    public const string CurveKind = "TemperatureCurve";
    public const string PowerCurveKind = "PowerCurve";
    public const double MinimumCurveTemperatureCelsius = -40;
    public const double MaximumCurveTemperatureCelsius = 125;
    public const double MinimumPowerCurveWatts = 0;
    public const double MaximumPowerCurveWatts = 1200;

    private string? _curvePointsText;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = ManualKind;

    public string Name { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    public bool HasCustomDescription { get; set; }

    public double Percent { get; set; } = AppSettings.LocalDefaultManualFanPercent;

    public List<FanCurvePoint> CurvePoints { get; set; } = [];

    public bool SmoothCurve { get; set; }

    public bool IsBuiltIn { get; set; }

    [JsonIgnore]
    public bool IsManual => string.Equals(Kind, ManualKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsTemperatureCurvePreset => string.Equals(Kind, CurveKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsPowerCurvePreset => string.Equals(Kind, PowerCurveKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsCurvePreset => IsTemperatureCurvePreset || IsPowerCurvePreset;

    [JsonIgnore]
    public bool IsPercentPreset =>
        IsManual || string.Equals(Kind, RestoreManualKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool CanEditPercent => IsPercentPreset;

    [JsonIgnore]
    public bool CanEditCurve => IsCurvePreset;

    [JsonIgnore]
    public bool CanDelete => true;

    [JsonIgnore]
    public bool CanSave => true;

    [JsonIgnore]
    public bool IsActive { get; private set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(NameKey) ? Name ?? string.Empty : LocalizationService.T(NameKey);

    [JsonIgnore]
    public string EditableName
    {
        get => DisplayName;
        set
        {
            Name = (value ?? string.Empty).Trim();
            NameKey = string.Empty;
        }
    }

    [JsonIgnore]
    public string EditableDetail
    {
        get => DisplayDetail;
        set
        {
            Description = (value ?? string.Empty).Trim();
            DescriptionKey = string.Empty;
            HasCustomDescription = true;
        }
    }

    [JsonIgnore]
    public string CurvePointsText
    {
        get
        {
            if (_curvePointsText is not null)
            {
                return _curvePointsText;
            }

            if (IsPowerCurvePreset)
            {
                return FormatPowerCurvePoints(CurvePoints);
            }

            return IsTemperatureCurvePreset || CurvePoints.Count > 0 ? FormatCurvePoints(CurvePoints) : string.Empty;
        }
        set => _curvePointsText = value ?? string.Empty;
    }

    [JsonIgnore]
    public string CurveEditorHeader => IsPowerCurvePreset
        ? LocalizationService.T("Control.PowerCurvePresetPoints")
        : LocalizationService.T("Preset.CurvePoints");

    [JsonIgnore]
    public string CurvePointsPlaceholder => IsPowerCurvePreset
        ? LocalizationService.T("Preset.PowerCurvePointsPlaceholder")
        : LocalizationService.T("Preset.CurvePointsPlaceholder");

    [JsonIgnore]
    public string CurvePreviewHeader => LocalizationService.T("Preset.CurvePreview");

    [JsonIgnore]
    public string CurveChartText => IsPowerCurvePreset
        ? BuildPowerCurveChartText(CurvePoints, SmoothCurve)
        : IsTemperatureCurvePreset
        ? BuildCurveChartText(CurvePoints, SmoothCurve)
        : LocalizationService.T("Preset.CurveUnused");

    [JsonIgnore]
    public string DisplayDetail
    {
        get
        {
            if (HasCustomDescription)
            {
                return Description ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(DescriptionKey))
            {
                return LocalizationService.T(DescriptionKey);
            }

            if (IsPowerCurvePreset)
            {
                return LocalizationService.T("Preset.PowerCurveDetail");
            }

            var defaultKey = DefaultDescriptionKey;
            return string.IsNullOrWhiteSpace(defaultKey)
                ? LocalizationService.T("Preset.CustomDetail")
                : LocalizationService.T(defaultKey);
        }
    }

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            if (string.Equals(Kind, RestoreManualKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.Format("Preset.RestoreManualSubtitle", Percent);
            }

            if (string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.DellAutoSubtitle");
            }

            if (IsPowerCurvePreset)
            {
                return LocalizationService.Format("Preset.PowerCurveSubtitle", CurvePoints.Count);
            }

            if (IsTemperatureCurvePreset)
            {
                return LocalizationService.Format("Preset.CurveSubtitle", CurvePoints.Count);
            }

            return LocalizationService.Format("Preset.ManualSubtitle", Percent);
        }
    }

    [JsonIgnore]
    public string CurrentMarker => IsActive ? LocalizationService.T("Preset.Current") : string.Empty;

    [JsonIgnore]
    public double CurrentMarkerOpacity => IsActive ? 1 : 0;

    [JsonIgnore]
    public string ApplyButtonLabel => LocalizationService.T("Preset.Apply");

    [JsonIgnore]
    public string SaveButtonLabel => LocalizationService.T("Preset.Save");

    [JsonIgnore]
    public string DeleteButtonLabel => LocalizationService.T("Preset.Delete");

    [JsonIgnore]
    public string IconGlyph => Id switch
    {
        "restore-manual" => "\uE777",
        "balanced" => "\uE9D9",
        "cooling" => "\uE9CA",
        "performance" => "\uE7C1",
        "dell-auto" => "\uE950",
        _ when string.Equals(Kind, PowerCurveKind, StringComparison.OrdinalIgnoreCase) => "\uE945",
        _ when string.Equals(Kind, CurveKind, StringComparison.OrdinalIgnoreCase) => "\uE9D2",
        _ => "\uE713",
    };

    [JsonIgnore]
    public string DefaultDescriptionKey => Id switch
    {
        "restore-manual" => "Preset.RestoreManualDetail",
        "balanced" => "Preset.BalancedDetail",
        "cooling" => "Preset.CoolingDetail",
        "performance" => "Preset.PerformanceDetail",
        "dell-auto" => "Preset.DellAutoDetail",
        _ when string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase) => "Preset.DellAutoDetail",
        _ when string.Equals(Kind, CurveKind, StringComparison.OrdinalIgnoreCase) => "Preset.CurveDetail",
        _ => "Preset.CustomDetail",
    };

    [JsonIgnore]
    public string ModeBadge
    {
        get
        {
            if (string.Equals(Kind, RestoreManualKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.RestoreManualBadge");
            }

            if (string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.DellAutoBadge");
            }

            if (IsPowerCurvePreset)
            {
                return LocalizationService.T("Preset.PowerCurveBadge");
            }

            if (IsTemperatureCurvePreset)
            {
                return LocalizationService.T("Preset.CurveBadge");
            }

            return LocalizationService.T("Preset.ManualBadge");
        }
    }

    [JsonIgnore]
    public string PrimaryMetric
    {
        get
        {
            if (string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.BmcMetric");
            }

            if (IsPowerCurvePreset)
            {
                return LocalizationService.T("SensorDisplay.PowerConsumption");
            }

            if (IsTemperatureCurvePreset)
            {
                return LocalizationService.T("Preset.CurveMetric");
            }

            return LocalizationService.Format("Preset.PercentMetric", Percent);
        }
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public FanPreset Clone()
    {
        var clone = new FanPreset
        {
            Id = Id,
            Kind = Kind,
            Name = Name,
            NameKey = NameKey,
            Description = Description,
            DescriptionKey = DescriptionKey,
            HasCustomDescription = HasCustomDescription,
            Percent = Percent,
            CurvePoints = CurvePoints.Select(point => point.Clone()).ToList(),
            SmoothCurve = SmoothCurve,
            IsBuiltIn = IsBuiltIn,
        };
        clone._curvePointsText = _curvePointsText;
        return clone;
    }

    public void ApplyCurvePointsText()
    {
        if (!IsCurvePreset)
        {
            _curvePointsText = null;
            CurvePoints.Clear();
            return;
        }

        CurvePoints = IsPowerCurvePreset
            ? ParsePowerCurvePoints(CurvePointsText)
            : ParseCurvePoints(CurvePointsText);
        _curvePointsText = null;
    }

    public void ValidateCurvePoints()
    {
        if (!IsCurvePreset)
        {
            return;
        }

        CurvePoints = IsPowerCurvePreset
            ? NormalizePowerCurvePoints(CurvePoints)
            : NormalizeCurvePoints(CurvePoints);
    }

    public int CalculateFanPercent(double temperatureCelsius)
    {
        return ToPercent(CalculateFanPercentValue(temperatureCelsius));
    }

    public double CalculateFanPercentValue(double temperatureCelsius)
    {
        if (!IsTemperatureCurvePreset)
        {
            throw new InvalidOperationException(LocalizationService.Format("Validation.CurvePresetKind", Kind));
        }

        var points = NormalizeCurvePoints(CurvePoints);
        return CalculateFanPercentFromCurvePoints(
            points,
            temperatureCelsius,
            SmoothCurve,
            usePowerAxis: false,
            "Validation.CurveEvaluationUnreachable");
    }

    public int CalculateFanPercentForPower(double powerWatts)
    {
        return ToPercent(CalculateFanPercentValueForPower(powerWatts));
    }

    public double CalculateFanPercentValueForPower(double powerWatts)
    {
        if (!IsPowerCurvePreset)
        {
            throw new InvalidOperationException(LocalizationService.Format("Validation.PowerCurvePresetKind", Kind));
        }

        var points = NormalizePowerCurvePoints(CurvePoints);
        return CalculateFanPercentFromCurvePoints(
            points,
            powerWatts,
            SmoothCurve,
            usePowerAxis: true,
            "Validation.PowerCurveEvaluationUnreachable");
    }

    public static List<FanCurvePoint> ParseCurvePoints(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.CurvePointCount"));
        }

        var points = new List<FanCurvePoint>();
        var normalizedText = text
            .Replace('，', ',')
            .Replace('：', ':')
            .Replace('％', '%')
            .Replace('℃', 'C');
        var rows = normalizedText.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var row in rows)
        {
            var match = Regex.Match(
                row,
                @"^(?<temp>-?\d+(?:\.\d+)?)\s*(?:°?\s*[cC])?\s*(?:=|:|,|->|=>|\s+)\s*(?<percent>\d+(?:\.\d+)?)\s*%?$");
            if (!match.Success)
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.CurvePointFormat", row));
            }

            points.Add(new FanCurvePoint
            {
                TemperatureCelsius = double.Parse(match.Groups["temp"].Value, CultureInfo.InvariantCulture),
                FanPercent = double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture),
            });
        }

        return NormalizeCurvePoints(points);
    }

    public static string FormatCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        return string.Join(
            Environment.NewLine,
            NormalizeCurvePoints(points)
                .Select(point => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.#} = {1:0.#}%",
                    point.TemperatureCelsius,
                    point.FanPercent)));
    }

    public static List<FanCurvePoint> ParsePowerCurvePoints(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.PowerCurvePointCount"));
        }

        var points = new List<FanCurvePoint>();
        var normalizedText = text
            .Replace('，', ',')
            .Replace('：', ':')
            .Replace('％', '%')
            .Replace('瓦', 'W');
        var rows = normalizedText.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var row in rows)
        {
            var match = Regex.Match(
                row,
                @"^(?<power>\d+(?:\.\d+)?)\s*(?:[wW])?\s*(?:=|:|,|->|=>|\s+)\s*(?<percent>\d+(?:\.\d+)?)\s*%?$");
            if (!match.Success)
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.PowerCurvePointFormat", row));
            }

            points.Add(new FanCurvePoint
            {
                PowerWatts = double.Parse(match.Groups["power"].Value, CultureInfo.InvariantCulture),
                FanPercent = double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture),
            });
        }

        return NormalizePowerCurvePoints(points);
    }

    public static string FormatPowerCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        return string.Join(
            Environment.NewLine,
            NormalizePowerCurvePoints(points)
                .Select(point => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.#}W = {1:0.#}%",
                    point.PowerWatts,
                    point.FanPercent)));
    }

    public static string BuildCurveChartText(IEnumerable<FanCurvePoint> points, bool smooth = false)
    {
        var normalized = NormalizeCurvePoints(points);
        var firstTemperature = normalized[0].TemperatureCelsius;
        var lastTemperature = normalized[^1].TemperatureCelsius;
        var minFan = normalized.Min(point => point.FanPercent);
        var maxFan = normalized.Max(point => point.FanPercent);
        const int samples = 18;
        const string bars = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";
        var glyphs = new char[samples];
        for (var index = 0; index < samples; index++)
        {
            var ratio = samples == 1 ? 0 : index / (double)(samples - 1);
            var temperature = firstTemperature + (lastTemperature - firstTemperature) * ratio;
            var fan = CalculateFanPercentFromPoints(normalized, temperature, smooth);
            var fanRatio = maxFan.Equals(minFan) ? 0 : (fan - minFan) / (maxFan - minFan);
            var glyphIndex = Math.Clamp((int)Math.Round(fanRatio * (bars.Length - 1), MidpointRounding.AwayFromZero), 0, bars.Length - 1);
            glyphs[index] = bars[glyphIndex];
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.#}C {1} {2:0.#}C  {3:0.#}-{4:0.#}%",
            firstTemperature,
            new string(glyphs),
            lastTemperature,
            minFan,
            maxFan);
    }

    public static string BuildPowerCurveChartText(IEnumerable<FanCurvePoint> points, bool smooth = false)
    {
        var normalized = NormalizePowerCurvePoints(points);
        var firstPower = normalized[0].PowerWatts;
        var lastPower = normalized[^1].PowerWatts;
        var minFan = normalized.Min(point => point.FanPercent);
        var maxFan = normalized.Max(point => point.FanPercent);
        const int samples = 18;
        const string bars = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";
        var glyphs = new char[samples];
        for (var index = 0; index < samples; index++)
        {
            var ratio = samples == 1 ? 0 : index / (double)(samples - 1);
            var power = firstPower + ((lastPower - firstPower) * ratio);
            var fan = CalculateFanPercentFromPowerPoints(normalized, power, smooth);
            var fanRatio = maxFan.Equals(minFan) ? 0 : (fan - minFan) / (maxFan - minFan);
            var glyphIndex = Math.Clamp((int)Math.Round(fanRatio * (bars.Length - 1), MidpointRounding.AwayFromZero), 0, bars.Length - 1);
            glyphs[index] = bars[glyphIndex];
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.#}W {1} {2:0.#}W  {3:0.#}-{4:0.#}%",
            firstPower,
            new string(glyphs),
            lastPower,
            minFan,
            maxFan);
    }

    private static double CalculateFanPercentFromPoints(IReadOnlyList<FanCurvePoint> points, double temperatureCelsius, bool smooth)
    {
        return CalculateFanPercentFromCurvePoints(
            points,
            temperatureCelsius,
            smooth,
            usePowerAxis: false,
            "Validation.CurveEvaluationUnreachable");
    }

    private static double CalculateFanPercentFromPowerPoints(IReadOnlyList<FanCurvePoint> points, double powerWatts, bool smooth)
    {
        return CalculateFanPercentFromCurvePoints(
            points,
            powerWatts,
            smooth,
            usePowerAxis: true,
            "Validation.PowerCurveEvaluationUnreachable");
    }

    private static double CalculateFanPercentFromCurvePoints(
        IReadOnlyList<FanCurvePoint> points,
        double input,
        bool smooth,
        bool usePowerAxis,
        string unreachableMessageKey)
    {
        static double AxisValue(FanCurvePoint point, bool powerAxis) =>
            powerAxis ? point.PowerWatts : point.TemperatureCelsius;

        var firstInput = AxisValue(points[0], usePowerAxis);
        if (input <= firstInput)
        {
            return points[0].FanPercent;
        }

        var last = points[^1];
        var lastInput = AxisValue(last, usePowerAxis);
        if (input >= lastInput)
        {
            return last.FanPercent;
        }

        var intervalIndex = 0;
        for (var index = 1; index < points.Count; index++)
        {
            if (input <= AxisValue(points[index], usePowerAxis))
            {
                intervalIndex = index - 1;
                break;
            }
        }

        if (intervalIndex < 0 || intervalIndex >= points.Count - 1)
        {
            throw new InvalidOperationException(LocalizationService.T(unreachableMessageKey));
        }

        var previous = points[intervalIndex];
        var current = points[intervalIndex + 1];
        var previousInput = AxisValue(previous, usePowerAxis);
        var currentInput = AxisValue(current, usePowerAxis);
        var span = currentInput - previousInput;
        var progress = Math.Clamp((input - previousInput) / span, 0, 1);
        if (!smooth || points.Count == 2)
        {
            return previous.FanPercent + ((current.FanPercent - previous.FanPercent) * progress);
        }

        var tangents = CalculateMonotoneTangents(points, usePowerAxis);
        var progressSquared = progress * progress;
        var progressCubed = progressSquared * progress;
        var startBasis = (2 * progressCubed) - (3 * progressSquared) + 1;
        var startTangentBasis = progressCubed - (2 * progressSquared) + progress;
        var endBasis = (-2 * progressCubed) + (3 * progressSquared);
        var endTangentBasis = progressCubed - progressSquared;
        var interpolated =
            (startBasis * previous.FanPercent) +
            (startTangentBasis * span * tangents[intervalIndex]) +
            (endBasis * current.FanPercent) +
            (endTangentBasis * span * tangents[intervalIndex + 1]);
        var minimum = Math.Min(previous.FanPercent, current.FanPercent);
        var maximum = Math.Max(previous.FanPercent, current.FanPercent);
        return Math.Clamp(interpolated, minimum, maximum);
    }

    private static double[] CalculateMonotoneTangents(IReadOnlyList<FanCurvePoint> points, bool usePowerAxis)
    {
        static double AxisValue(FanCurvePoint point, bool powerAxis) =>
            powerAxis ? point.PowerWatts : point.TemperatureCelsius;

        var intervalCount = points.Count - 1;
        var spans = new double[intervalCount];
        var slopes = new double[intervalCount];
        for (var index = 0; index < intervalCount; index++)
        {
            spans[index] = AxisValue(points[index + 1], usePowerAxis) - AxisValue(points[index], usePowerAxis);
            slopes[index] = (points[index + 1].FanPercent - points[index].FanPercent) / spans[index];
        }

        var tangents = new double[points.Count];
        if (points.Count == 2)
        {
            tangents[0] = slopes[0];
            tangents[1] = slopes[0];
            return tangents;
        }

        tangents[0] = CalculateEndpointTangent(spans[0], spans[1], slopes[0], slopes[1]);
        tangents[^1] = CalculateEndpointTangent(
            spans[^1],
            spans[^2],
            slopes[^1],
            slopes[^2]);

        for (var index = 1; index < points.Count - 1; index++)
        {
            var previousSlope = slopes[index - 1];
            var nextSlope = slopes[index];
            if (previousSlope == 0 || nextSlope == 0 || Math.Sign(previousSlope) != Math.Sign(nextSlope))
            {
                tangents[index] = 0;
                continue;
            }

            var previousSpan = spans[index - 1];
            var nextSpan = spans[index];
            var firstWeight = (2 * nextSpan) + previousSpan;
            var secondWeight = nextSpan + (2 * previousSpan);
            tangents[index] = (firstWeight + secondWeight) /
                ((firstWeight / previousSlope) + (secondWeight / nextSlope));
        }

        return tangents;
    }

    private static double CalculateEndpointTangent(
        double endpointSpan,
        double adjacentSpan,
        double endpointSlope,
        double adjacentSlope)
    {
        var tangent = (((2 * endpointSpan) + adjacentSpan) * endpointSlope -
            (endpointSpan * adjacentSlope)) /
            (endpointSpan + adjacentSpan);
        if (Math.Sign(tangent) != Math.Sign(endpointSlope))
        {
            return 0;
        }

        if (Math.Sign(endpointSlope) != Math.Sign(adjacentSlope) &&
            Math.Abs(tangent) > Math.Abs(3 * endpointSlope))
        {
            return 3 * endpointSlope;
        }

        return tangent;
    }

    private static List<FanCurvePoint> NormalizeCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        var normalized = points
            .Select(point => point.Clone())
            .OrderBy(point => point.TemperatureCelsius)
            .ToList();

        if (normalized.Count < 2)
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.CurvePointCount"));
        }

        for (var index = 0; index < normalized.Count; index++)
        {
            var point = normalized[index];
            if (double.IsNaN(point.TemperatureCelsius) ||
                double.IsInfinity(point.TemperatureCelsius) ||
                point.TemperatureCelsius is < MinimumCurveTemperatureCelsius or > MaximumCurveTemperatureCelsius)
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.CurveTemperatureRange", point.TemperatureCelsius));
            }

            if (double.IsNaN(point.FanPercent) ||
                double.IsInfinity(point.FanPercent) ||
                point.FanPercent is < 0 or > 100)
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.CurveFanPercentRange", point.FanPercent));
            }

            if (index > 0 && normalized[index - 1].TemperatureCelsius.Equals(point.TemperatureCelsius))
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.CurvePointDuplicate", point.TemperatureCelsius));
            }
        }

        return normalized;
    }

    private static List<FanCurvePoint> NormalizePowerCurvePoints(IEnumerable<FanCurvePoint> points)
    {
        var normalized = points
            .Select(point => point.Clone())
            .OrderBy(point => point.PowerWatts)
            .ToList();

        if (normalized.Count < 2)
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.PowerCurvePointCount"));
        }

        for (var index = 0; index < normalized.Count; index++)
        {
            var point = normalized[index];
            if (double.IsNaN(point.PowerWatts) ||
                double.IsInfinity(point.PowerWatts) ||
                point.PowerWatts is < MinimumPowerCurveWatts or > MaximumPowerCurveWatts)
            {
                throw new InvalidOperationException(LocalizationService.Format(
                    "Validation.PowerCurveWattsRange",
                    MinimumPowerCurveWatts,
                    MaximumPowerCurveWatts,
                    point.PowerWatts));
            }

            if (double.IsNaN(point.FanPercent) ||
                double.IsInfinity(point.FanPercent) ||
                point.FanPercent is < 0 or > 100)
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.CurveFanPercentRange", point.FanPercent));
            }

            if (index > 0 && normalized[index - 1].PowerWatts.Equals(point.PowerWatts))
            {
                throw new InvalidOperationException(LocalizationService.Format("Validation.PowerCurvePointDuplicate", point.PowerWatts));
            }
        }

        return normalized;
    }

    private static int ToPercent(double percent)
    {
        var rounded = (int)Math.Round(percent, MidpointRounding.AwayFromZero);
        AppSettings.ValidatePercent(rounded, nameof(percent));
        return rounded;
    }

    public static List<FanPreset> CreateDefaultPresets()
    {
        return
        [
            new FanPreset
            {
                Id = "restore-manual",
                Kind = RestoreManualKind,
                NameKey = "Control.Default",
                DescriptionKey = "Preset.RestoreManualDetail",
                Percent = AppSettings.LocalDefaultManualFanPercent,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "balanced",
                Kind = ManualKind,
                NameKey = "Control.Balanced",
                DescriptionKey = "Preset.BalancedDetail",
                Percent = 20,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "cooling",
                Kind = ManualKind,
                NameKey = "Control.Cooling",
                DescriptionKey = "Preset.CoolingDetail",
                Percent = 35,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "performance",
                Kind = ManualKind,
                NameKey = "Control.Performance",
                DescriptionKey = "Preset.PerformanceDetail",
                Percent = 50,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "dell-auto",
                Kind = DellAutoKind,
                NameKey = "Control.DellAuto",
                DescriptionKey = "Preset.DellAutoDetail",
                Percent = 0,
                IsBuiltIn = true,
            },
        ];
    }
}
