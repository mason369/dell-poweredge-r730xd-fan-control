using System;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DellR730xdFanControlCenter;

public sealed class LogEntry
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    public string Level { get; set; } = LocalizationService.T("Log.Info");

    public string SemanticLevel { get; set; } = "Info";

    public string Message { get; set; } = string.Empty;

    public string DisplayTime => Time.ToString("HH:mm:ss");

    public SolidColorBrush LevelForegroundBrush => ToBrush(Style.ForegroundHex);

    public SolidColorBrush LevelBackgroundBrush => ToBrush(Style.BackgroundHex);

    public SolidColorBrush LevelBorderBrush => ToBrush(Style.BorderHex);

    private LogLevelStyle Style => LogLevelStyle.FromSemanticLevel(SemanticLevel);

    private static SolidColorBrush ToBrush(string hex)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException(LocalizationService.Format("Dashboard.MetricColorInvalid", hex));
        }

        var alpha = Convert.ToByte(hex.Substring(1, 2), 16);
        var red = Convert.ToByte(hex.Substring(3, 2), 16);
        var green = Convert.ToByte(hex.Substring(5, 2), 16);
        var blue = Convert.ToByte(hex.Substring(7, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
