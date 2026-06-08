using System;
using System.Collections.Generic;

namespace DellR730xdFanControlCenter;

public sealed class AppLogRecord
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; set; }

    public string Level { get; set; } = "Info";

    public string Category { get; set; } = "Application";

    public string EventName { get; set; } = "Message";

    public string Message { get; set; } = string.Empty;

    public string? OperationId { get; set; }

    public string? OperationName { get; set; }

    public string? Phase { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public double? DurationMilliseconds { get; set; }

    public bool? Succeeded { get; set; }

    public int? ExitCode { get; set; }

    public string? CommandLine { get; set; }

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }

    public IReadOnlyDictionary<string, string>? Properties { get; set; }
}
