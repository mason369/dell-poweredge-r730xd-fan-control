using System;

namespace DellR730xdFanControlCenter;

public sealed class LogEntry
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    public string Level { get; set; } = "Info";

    public string Message { get; set; } = string.Empty;

    public string DisplayTime => Time.ToString("HH:mm:ss");
}
