using System;

namespace DellR730xdFanControlCenter;

public sealed class CommandTraceEventArgs : EventArgs
{
    public required string CommandLine { get; init; }

    public required bool Succeeded { get; init; }

    public required int ExitCode { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }
}
