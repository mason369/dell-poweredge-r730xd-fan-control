using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DellR730xdFanControlCenter;

public sealed class SettingsStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellR730xdFanControlCenter"))
    {
    }

    public SettingsStore(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            throw new ArgumentException("设置目录不能为空。", nameof(settingsDirectory));
        }

        SettingsDirectory = settingsDirectory;
    }

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public bool SettingsFileExists => File.Exists(SettingsPath);

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
            ?? throw new InvalidOperationException($"设置文件为空或格式无效：{SettingsPath}");
        Normalize(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.IpmiToolPath = AppSettings.BundledIpmiToolRelativePath;

        if (settings.SensorRefreshSeconds < 1)
        {
            settings.SensorRefreshSeconds = 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Language) || !LocalizationService.IsSupportedLanguage(settings.Language))
        {
            settings.Language = LocalizationService.DefaultLanguage;
        }

        settings.Presets = NormalizePresets(settings.Presets);
        settings.LastRunningPresetId = (settings.LastRunningPresetId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(settings.LastRunningPresetId))
        {
            settings.LastSmartAutoPolicyRunning = false;
        }
    }

    private static List<FanPreset> NormalizePresets(List<FanPreset>? presets)
    {
        if (presets is null)
        {
            return FanPreset.CreateDefaultPresets();
        }

        var normalized = new List<FanPreset>();
        var builtInDefaults = FanPreset.CreateDefaultPresets();
        foreach (var preset in presets)
        {
            var clone = preset.Clone();
            if (string.IsNullOrWhiteSpace(clone.Id))
            {
                clone.Id = Guid.NewGuid().ToString("N");
            }

            var builtInDefault = builtInDefaults.Find(item => item.Id.Equals(clone.Id, StringComparison.OrdinalIgnoreCase));
            if (builtInDefault is not null)
            {
                clone.IsBuiltIn = builtInDefault.IsBuiltIn;
                clone.Kind = builtInDefault.Kind;

                if (string.IsNullOrWhiteSpace(clone.Name) && string.IsNullOrWhiteSpace(clone.NameKey))
                {
                    clone.NameKey = builtInDefault.NameKey;
                }

                if (!clone.HasCustomDescription && string.IsNullOrWhiteSpace(clone.DescriptionKey))
                {
                    clone.DescriptionKey = builtInDefault.DescriptionKey;
                }
            }

            NormalizePreset(clone);

            var existing = normalized.FindIndex(item => item.Id.Equals(clone.Id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                normalized[existing] = clone;
            }
            else
            {
                normalized.Add(clone);
            }
        }

        return normalized;
    }

    private static void NormalizePreset(FanPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Kind))
        {
            throw new InvalidOperationException("风扇预设类型为空。");
        }

        if (preset.Kind.Equals(FanPreset.ManualKind, StringComparison.OrdinalIgnoreCase))
        {
            preset.Kind = FanPreset.ManualKind;
            AppSettings.ValidatePercent((int)Math.Round(preset.Percent, MidpointRounding.AwayFromZero), nameof(preset.Percent));
            preset.CurvePoints.Clear();
        }
        else if (preset.Kind.Equals(FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
        {
            preset.Kind = FanPreset.RestoreManualKind;
            AppSettings.ValidatePercent((int)Math.Round(preset.Percent, MidpointRounding.AwayFromZero), nameof(preset.Percent));
            preset.CurvePoints.Clear();
        }
        else if (preset.Kind.Equals(FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
        {
            preset.Kind = FanPreset.DellAutoKind;
            preset.Percent = 0;
            preset.CurvePoints.Clear();
        }
        else if (preset.Kind.Equals(FanPreset.CurveKind, StringComparison.OrdinalIgnoreCase))
        {
            preset.Kind = FanPreset.CurveKind;
            preset.Percent = 0;
            preset.ApplyCurvePointsText();
            preset.ValidateCurvePoints();
        }
        else if (preset.Kind.Equals(FanPreset.PowerCurveKind, StringComparison.OrdinalIgnoreCase))
        {
            preset.Kind = FanPreset.PowerCurveKind;
            preset.Percent = 0;
            preset.ApplyCurvePointsText();
            preset.ValidateCurvePoints();
        }
        else
        {
            throw new InvalidOperationException($"不支持的风扇预设类型：{preset.Kind}");
        }

        if (string.IsNullOrWhiteSpace(preset.NameKey) && string.IsNullOrWhiteSpace(preset.Name))
        {
            throw new InvalidOperationException("风扇预设名称不能为空。");
        }

        preset.Name = (preset.Name ?? string.Empty).Trim();
        preset.Description = (preset.Description ?? string.Empty).Trim();
    }

    public string ProtectPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return string.Empty;
        }

        var rawBytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string UnprotectPassword(string protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return string.Empty;
        }

        var protectedBytes = Convert.FromBase64String(protectedPassword);
        var rawBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(rawBytes);
    }
}
