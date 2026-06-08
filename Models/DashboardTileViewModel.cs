using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DellR730xdFanControlCenter;

public sealed class DashboardTileViewModel
{
    public string Title { get; set; } = string.Empty;

    public string Value { get; set; } = "--";

    public string Unit { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string AccentHex { get; set; } = "#FF64748B";

    public string ValueHex { get; set; } = "#FF172033";

    public double TemperatureIconOpacity { get; set; }

    public double FanIconOpacity { get; set; }

    public double PowerIconOpacity { get; set; }

    public double VoltageIconOpacity { get; set; }

    public double CurrentIconOpacity { get; set; }

    public double HealthIconOpacity { get; set; }

    public bool IsFanAnimated { get; set; }

    public double FanRotationSeconds { get; set; } = 2;

    public double ElectricalIconOpacity { get; set; }

    public bool IsElectricalAnimated { get; set; }

    public double ElectricalPulseSeconds { get; set; } = 0.9;

    public SolidColorBrush AccentBrush => ToBrush(AccentHex);

    public SolidColorBrush IconBackgroundBrush => ToBrush(WithAlpha(AccentHex, "20"));

    public SolidColorBrush ValueBrush => ToBrush(ValueHex);

    private static string WithAlpha(string hex, string alpha)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException($"Invalid dashboard tile color value: {hex}");
        }

        return $"#{alpha}{hex[3..]}";
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException($"Invalid dashboard tile color value: {hex}");
        }

        var alpha = Convert.ToByte(hex.Substring(1, 2), 16);
        var red = Convert.ToByte(hex.Substring(3, 2), 16);
        var green = Convert.ToByte(hex.Substring(5, 2), 16);
        var blue = Convert.ToByte(hex.Substring(7, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
