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

    public string Description { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    public bool HasCustomDescription { get; set; }

    public double Percent { get; set; } = AppSettings.LocalDefaultManualFanPercent;

    public bool IsBuiltIn { get; set; }

    [JsonIgnore]
    public bool IsManual => string.Equals(Kind, ManualKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsPercentPreset =>
        IsManual || string.Equals(Kind, RestoreManualKind, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool CanEditPercent => IsPercentPreset;

    [JsonIgnore]
    public bool CanDelete => !IsBuiltIn;

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
    public string DefaultDescriptionKey => Id switch
    {
        "restore-manual" => "Preset.RestoreManualDetail",
        "balanced" => "Preset.BalancedDetail",
        "cooling" => "Preset.CoolingDetail",
        "performance" => "Preset.PerformanceDetail",
        "dell-auto" => "Preset.DellAutoDetail",
        _ when string.Equals(Kind, DellAutoKind, StringComparison.OrdinalIgnoreCase) => "Preset.DellAutoDetail",
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
            Description = Description,
            DescriptionKey = DescriptionKey,
            HasCustomDescription = HasCustomDescription,
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
