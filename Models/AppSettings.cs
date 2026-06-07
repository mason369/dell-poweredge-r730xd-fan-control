using System;

namespace DellR730xdFanControlCenter;

public sealed class AppSettings
{
    public const int LocalDefaultManualFanPercent = 10;

    public const string BundledIpmiToolRelativePath = @"BundledTools\ipmitool\ipmitool.exe";

    public string Host { get; set; } = "192.168.1.73";

    public string UserName { get; set; } = "root";

    public bool RememberPassword { get; set; }

    public string ProtectedPassword { get; set; } = string.Empty;

    public string IpmiToolPath { get; set; } = BundledIpmiToolRelativePath;

    public int FanCount { get; set; } = 6;

    public int DefaultAllFanPercent { get; set; } = LocalDefaultManualFanPercent;

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool EnableIndividualFanTargets { get; set; }

    public int SensorRefreshSeconds { get; set; } = 1;

    public int CommandTimeoutSeconds { get; set; } = 35;

    public double TargetCpuTemperatureCelsius { get; set; } = 68;

    public double HighCpuTemperatureCelsius { get; set; } = 78;

    public double EmergencyCpuTemperatureCelsius { get; set; } = 84;

    public int AutoMinimumFanPercent { get; set; } = LocalDefaultManualFanPercent;

    public int AutoMaximumFanPercent { get; set; } = 42;

    public string Theme { get; set; } = "Default";

    public static void ValidatePercent(int percent, string fieldName)
    {
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(fieldName, percent, "Fan speed must be between 0 and 100 percent.");
        }
    }
}
