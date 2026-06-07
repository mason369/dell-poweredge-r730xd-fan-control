using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DellR730xdFanControlCenter;

public sealed class FanPreset
{
    public const string ManualKind = "Manual";
    public const string RestoreManualKind = "RestoreManual";
    public const string DellAutoKind = "DellAuto";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = ManualKind;

    public string Name { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public double Percent { get; set; } = AppSettings.LocalDefaultManualFanPercent;

    public bool IsBuiltIn { get; set; }

    [JsonIgnore]
    public bool IsManual => string.Equals(Kind, ManualKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool CanEditPercent => IsManual;

    [JsonIgnore]
    public bool CanDelete => !IsBuiltIn;

    [JsonIgnore]
    public bool IsActive { get; private set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(NameKey) ? Name : LocalizationService.T(NameKey);

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
        _ => "\uE713",
    };

    [JsonIgnore]
    public string Detail
    {
        get
        {
            if (string.Equals(Id, "restore-manual", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.RestoreManualDetail");
            }

            if (string.Equals(Id, "balanced", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.BalancedDetail");
            }

            if (string.Equals(Id, "cooling", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.CoolingDetail");
            }

            if (string.Equals(Id, "performance", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.PerformanceDetail");
            }

            if (string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.T("Preset.DellAutoDetail");
            }

            return LocalizationService.T("Preset.CustomDetail");
        }
    }

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

            return LocalizationService.T("Preset.ManualBadge");
        }
    }

    [JsonIgnore]
    public string PrimaryMetric => string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase)
        ? LocalizationService.T("Preset.BmcMetric")
        : LocalizationService.Format("Preset.PercentMetric", Percent);

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public FanPreset Clone()
    {
        return new FanPreset
        {
            Id = Id,
            Kind = Kind,
            Name = Name,
            NameKey = NameKey,
            Percent = Percent,
            IsBuiltIn = IsBuiltIn,
        };
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
                Percent = AppSettings.LocalDefaultManualFanPercent,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "balanced",
                Kind = ManualKind,
                NameKey = "Control.Balanced",
                Percent = 20,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "cooling",
                Kind = ManualKind,
                NameKey = "Control.Cooling",
                Percent = 35,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "performance",
                Kind = ManualKind,
                NameKey = "Control.Performance",
                Percent = 50,
                IsBuiltIn = true,
            },
            new FanPreset
            {
                Id = "dell-auto",
                Kind = DellAutoKind,
                NameKey = "Control.DellAuto",
                Percent = 0,
                IsBuiltIn = true,
            },
        ];
    }
}
