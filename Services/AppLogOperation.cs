using System;
using System.Collections.Generic;

namespace DellR730xdFanControlCenter;

public sealed class AppLogOperation
{
    private readonly AppLogService _log;
    private bool _completed;

    internal AppLogOperation(AppLogService log, string operationId, string operationName, DateTimeOffset startedAt)
    {
        _log = log;
        OperationId = operationId;
        OperationName = operationName;
        StartedAt = startedAt;
    }

    public string OperationId { get; }

    public string OperationName { get; }

    public DateTimeOffset StartedAt { get; }

    public void Succeed(string message, IReadOnlyDictionary<string, string>? properties = null)
    {
        Complete("Info", "Succeeded", true, message, exception: null, properties);
    }

    public void Fail(Exception exception, IReadOnlyDictionary<string, string>? properties = null)
    {
        Complete("Error", "Failed", false, exception.Message, exception, properties);
    }

    private void Complete(
        string level,
        string phase,
        bool succeeded,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, string>? properties)
    {
        if (_completed)
        {
            throw new InvalidOperationException($"操作日志 {OperationId} 已经完成，不能重复结束。");
        }

        _log.WriteOperationTerminalRecord(this, level, phase, succeeded, message, exception, properties);
        _completed = true;
    }
}
