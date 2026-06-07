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
    public string ApplyButtonLabel => LocalizationService.T("Preset.Apply");

    [JsonIgnore]
    public string SaveButtonLabel => LocalizationService.T("Preset.Save");

    [JsonIgnore]
    public string DeleteButtonLabel => LocalizationService.T("Preset.Delete");

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
