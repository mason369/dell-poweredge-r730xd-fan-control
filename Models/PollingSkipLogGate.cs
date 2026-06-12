namespace DellR730xdFanControlCenter;

public enum PollingSkipKind
{
    PreviousPollRunning,
    IpmiCommandBusy,
}

public sealed class PollingSkipLogGate
{
    private bool _previousPollRunningLogged;
    private bool _ipmiCommandBusyLogged;

    public static bool OpenTopStatusForSkippedTick => false;

    public bool ShouldLog(PollingSkipKind kind)
    {
        return kind switch
        {
            PollingSkipKind.PreviousPollRunning => ShouldLogPreviousPollRunning(),
            PollingSkipKind.IpmiCommandBusy => ShouldLogIpmiCommandBusy(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的轮询跳过类型。"),
        };
    }

    public void Reset(PollingSkipKind kind)
    {
        switch (kind)
        {
            case PollingSkipKind.PreviousPollRunning:
                _previousPollRunningLogged = false;
                break;
            case PollingSkipKind.IpmiCommandBusy:
                _ipmiCommandBusyLogged = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的轮询跳过类型。");
        }
    }

    public void ResetAll()
    {
        _previousPollRunningLogged = false;
        _ipmiCommandBusyLogged = false;
    }

    private bool ShouldLogPreviousPollRunning()
    {
        if (_previousPollRunningLogged)
        {
            return false;
        }

        _previousPollRunningLogged = true;
        return true;
    }

    private bool ShouldLogIpmiCommandBusy()
    {
        if (_ipmiCommandBusyLogged)
        {
            return false;
        }

        _ipmiCommandBusyLogged = true;
        return true;
    }
}
