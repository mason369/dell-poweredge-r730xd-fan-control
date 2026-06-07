using System;
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
