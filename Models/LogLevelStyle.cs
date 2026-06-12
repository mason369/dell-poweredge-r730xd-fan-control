using System;

namespace DellR730xdFanControlCenter;

public sealed record LogLevelStyle(
    string SemanticName,
    string ForegroundHex,
    string BackgroundHex,
    string BorderHex)
{
    public static LogLevelStyle FromDisplayLevel(string level)
    {
        var token = Normalize(level);
        return token switch
        {
            "error" or "fail" or "failed" or "错误" or "失败" => Error,
            "warn" or "warning" or "警告" => Warning,
            "ok" or "success" or "succeeded" or "成功" => Success,
            _ => Info,
        };
    }

    public static LogLevelStyle FromSemanticLevel(string level)
    {
        var token = Normalize(level);
        return token switch
        {
            "info" => Info,
            "warning" or "warn" => Warning,
            "success" or "ok" => Success,
            "error" or "fail" or "failed" => Error,
            _ => throw new ArgumentException(LocalizationService.Format("Log.UnsupportedSemanticLevel", level), nameof(level)),
        };
    }

    private static string Normalize(string level)
    {
        return string.IsNullOrWhiteSpace(level)
            ? string.Empty
            : level.Trim().ToLowerInvariant();
    }

    private static readonly LogLevelStyle Info = new(
        "Info",
        "#FF2563EB",
        "#1A2563EB",
        "#662563EB");

    private static readonly LogLevelStyle Warning = new(
        "Warning",
        "#FFB45309",
        "#1AB45309",
        "#66B45309");

    private static readonly LogLevelStyle Success = new(
        "Success",
        "#FF15803D",
        "#1A15803D",
        "#6615803D");

    private static readonly LogLevelStyle Error = new(
        "Error",
        "#FFB42318",
        "#1AB42318",
        "#66B42318");
}
