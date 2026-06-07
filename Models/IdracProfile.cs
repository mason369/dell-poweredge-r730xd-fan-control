namespace DellR730xdFanControlCenter;

public sealed class IdracProfile
{
    public required string Host { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }

    public required string IpmiToolPath { get; init; }

    public int CommandTimeoutSeconds { get; init; } = 35;
}
