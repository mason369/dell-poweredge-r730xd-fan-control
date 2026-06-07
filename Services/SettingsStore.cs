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

    public string SettingsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellR730xdFanControlCenter");

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
            ?? throw new InvalidOperationException($"Settings file is empty or invalid: {SettingsPath}");
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
    }

    private static List<FanPreset> NormalizePresets(List<FanPreset>? presets)
    {
        var normalized = FanPreset.CreateDefaultPresets();
        if (presets is null || presets.Count == 0)
        {
            return normalized;
        }

        foreach (var preset in presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(preset.Kind))
            {
                throw new InvalidOperationException("Fan preset kind is empty.");
            }

            if (!preset.Kind.Equals(FanPreset.ManualKind, StringComparison.OrdinalIgnoreCase) &&
                !preset.Kind.Equals(FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase) &&
                !preset.Kind.Equals(FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported fan preset kind: {preset.Kind}");
            }

            if (preset.IsManual)
            {
                preset.Kind = FanPreset.ManualKind;
                AppSettings.ValidatePercent((int)Math.Round(preset.Percent, MidpointRounding.AwayFromZero), nameof(preset.Percent));
            }
            else if (preset.Kind.Equals(FanPreset.RestoreManualKind, StringComparison.OrdinalIgnoreCase))
            {
                preset.Kind = FanPreset.RestoreManualKind;
                preset.Percent = AppSettings.LocalDefaultManualFanPercent;
            }
            else if (preset.Kind.Equals(FanPreset.DellAutoKind, StringComparison.OrdinalIgnoreCase))
            {
                preset.Kind = FanPreset.DellAutoKind;
                preset.Percent = 0;
            }

            if (string.IsNullOrWhiteSpace(preset.NameKey) && string.IsNullOrWhiteSpace(preset.Name))
            {
                throw new InvalidOperationException("Fan preset name is required.");
            }

            preset.Name = preset.Name.Trim();

            var existing = normalized.FindIndex(item => item.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                var replacement = preset.Clone();
                replacement.IsBuiltIn = normalized[existing].IsBuiltIn;
                replacement.NameKey = string.IsNullOrWhiteSpace(normalized[existing].NameKey)
                    ? replacement.NameKey
                    : normalized[existing].NameKey;
                replacement.Kind = normalized[existing].Kind;
                normalized[existing] = replacement;
            }
            else
            {
                normalized.Add(preset.Clone());
            }
        }

        return normalized;
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
