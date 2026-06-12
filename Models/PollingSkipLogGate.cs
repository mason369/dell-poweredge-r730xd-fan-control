namespace DellR730xdFanControlCenter;

public enum PollingSkipKind
{
    PreviousPollRunning,
    IpmiCommandBusy,
    AutoPolicyTickRunning,
    AutoPolicyIpmiBusy,
}

public sealed class PollingSkipLogGate
{
    private bool _previousPollRunningLogged;
    private bool _ipmiCommandBusyLogged;
    private bool _autoPolicyTickRunningLogged;
    private bool _autoPolicyIpmiBusyLogged;

    public static bool OpenTopStatusForSkippedTick => false;

    public bool ShouldLog(PollingSkipKind kind)
    {
        return kind switch
        {
            PollingSkipKind.PreviousPollRunning => ShouldLogPreviousPollRunning(),
            PollingSkipKind.IpmiCommandBusy => ShouldLogIpmiCommandBusy(),
            PollingSkipKind.AutoPolicyTickRunning => ShouldLogAutoPolicyTickRunning(),
            PollingSkipKind.AutoPolicyIpmiBusy => ShouldLogAutoPolicyIpmiBusy(),
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
            case PollingSkipKind.AutoPolicyTickRunning:
                _autoPolicyTickRunningLogged = false;
                break;
            case PollingSkipKind.AutoPolicyIpmiBusy:
                _autoPolicyIpmiBusyLogged = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的轮询跳过类型。");
        }
    }

    public void ResetAll()
    {
        _previousPollRunningLogged = false;
        _ipmiCommandBusyLogged = false;
        _autoPolicyTickRunningLogged = false;
        _autoPolicyIpmiBusyLogged = false;
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

    private bool ShouldLogAutoPolicyTickRunning()
    {
        if (_autoPolicyTickRunningLogged)
        {
            return false;
        }

        _autoPolicyTickRunningLogged = true;
        return true;
    }

    private bool ShouldLogAutoPolicyIpmiBusy()
    {
        if (_autoPolicyIpmiBusyLogged)
        {
            return false;
        }

        _autoPolicyIpmiBusyLogged = true;
        return true;
    }
}
