using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DellR730xdFanControlCenter;

public sealed class AppLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly object _writeLock = new();
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _lastTimestamp;

    public AppLogService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellR730xdFanControlCenter", "logs"))
    {
    }

    public AppLogService(string logDirectory)
        : this(logDirectory, () => DateTimeOffset.Now)
    {
    }

    public AppLogService(string logDirectory, Func<DateTimeOffset> clock)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(logDirectory));
        }

        LogDirectory = logDirectory;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string LogDirectory { get; }

    public string CurrentLogPath
    {
        get
        {
            var timestamp = _lastTimestamp ?? _clock();
            return BuildLogPath(timestamp);
        }
    }

    public void Write(AppLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var timestamp = record.Timestamp == default ? _clock() : record.Timestamp;
        record.Timestamp = timestamp;

        if (string.IsNullOrWhiteSpace(record.EventId))
        {
            record.EventId = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(record.Level))
        {
            throw new InvalidOperationException("日志记录级别不能为空。");
        }

        if (string.IsNullOrWhiteSpace(record.Category))
        {
            throw new InvalidOperationException("日志记录分类不能为空。");
        }

        if (string.IsNullOrWhiteSpace(record.EventName))
        {
            throw new InvalidOperationException("日志记录事件名不能为空。");
        }

        var line = JsonSerializer.Serialize(record, JsonOptions);
        var logPath = BuildLogPath(timestamp);

        lock (_writeLock)
        {
            Directory.CreateDirectory(LogDirectory);
            using var stream = new FileStream(
                logPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Append,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                });
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(line);
            _lastTimestamp = timestamp;
        }
    }

    public AppLogOperation StartOperation(
        string operationName,
        string message,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException("操作名称不能为空。", nameof(operationName));
        }

        var startedAt = _clock();
        var operation = new AppLogOperation(this, Guid.NewGuid().ToString("N"), operationName, startedAt);
        Write(new AppLogRecord
        {
            Timestamp = startedAt,
            Level = "Info",
            Category = "Operation",
            EventName = operationName,
            Message = message,
            OperationId = operation.OperationId,
            OperationName = operationName,
            Phase = "Started",
            StartedAt = startedAt,
            Properties = properties,
        });

        return operation;
    }

    internal void WriteOperationTerminalRecord(
        AppLogOperation operation,
        string level,
        string phase,
        bool succeeded,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, string>? properties)
    {
        var finishedAt = _clock();
        Write(new AppLogRecord
        {
            Timestamp = finishedAt,
            Level = level,
            Category = "Operation",
            EventName = operation.OperationName,
            Message = message,
            OperationId = operation.OperationId,
            OperationName = operation.OperationName,
            Phase = phase,
            StartedAt = operation.StartedAt,
            FinishedAt = finishedAt,
            DurationMilliseconds = (finishedAt - operation.StartedAt).TotalMilliseconds,
            Succeeded = succeeded,
            ErrorType = exception?.GetType().Name,
            ErrorMessage = exception?.Message,
            Properties = properties,
        });
    }

    private string BuildLogPath(DateTimeOffset timestamp)
    {
        return Path.Combine(LogDirectory, $"runtime-{timestamp:yyyyMMdd}.jsonl");
    }
}
