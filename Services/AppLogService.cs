using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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
    private readonly Channel<AppLogRecord> _writeQueue;
    private readonly Task _writeWorker;
    private readonly object _flushLock = new();
    private int _pendingWriteCount;
    private DateTimeOffset? _lastTimestamp;
    private TaskCompletionSource? _flushCompletion;

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
        _writeQueue = Channel.CreateUnbounded<AppLogRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _writeWorker = Task.Run(ProcessWriteQueueAsync);
    }

    public string LogDirectory { get; }

    public event EventHandler<Exception>? WriteFailed;

    public string CurrentLogPath
    {
        get
        {
            DateTimeOffset timestamp;
            lock (_writeLock)
            {
                timestamp = _lastTimestamp ?? _clock();
            }

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

        Interlocked.Increment(ref _pendingWriteCount);
        if (!_writeQueue.Writer.TryWrite(record))
        {
            Interlocked.Decrement(ref _pendingWriteCount);
            throw new InvalidOperationException("无法排队写入运行日志。");
        }
    }

    public Task FlushAsync()
    {
        if (Volatile.Read(ref _pendingWriteCount) == 0)
        {
            return Task.CompletedTask;
        }

        lock (_flushLock)
        {
            if (Volatile.Read(ref _pendingWriteCount) == 0)
            {
                return Task.CompletedTask;
            }

            _flushCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _flushCompletion.Task;
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

    private async Task ProcessWriteQueueAsync()
    {
        await foreach (var record in _writeQueue.Reader.ReadAllAsync())
        {
            try
            {
                WriteRecord(record);
            }
            catch (Exception ex)
            {
                WriteFailed?.Invoke(this, ex);
            }
            finally
            {
                if (Interlocked.Decrement(ref _pendingWriteCount) == 0)
                {
                    CompleteFlushWaiter();
                }
            }
        }
    }

    private void WriteRecord(AppLogRecord record)
    {
        var line = JsonSerializer.Serialize(record, JsonOptions);
        var logPath = BuildLogPath(record.Timestamp);

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
            _lastTimestamp = record.Timestamp;
        }
    }

    private void CompleteFlushWaiter()
    {
        TaskCompletionSource? completion;
        lock (_flushLock)
        {
            completion = _flushCompletion;
            _flushCompletion = null;
        }

        completion?.TrySetResult();
    }
}
