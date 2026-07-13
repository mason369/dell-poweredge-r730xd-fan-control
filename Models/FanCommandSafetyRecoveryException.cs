using System;

namespace DellR730xdFanControlCenter;

public sealed class FanCommandSafetyRecoveryException : InvalidOperationException
{
    public FanCommandSafetyRecoveryException(string message, Exception fanCommandFailure)
        : base(message, fanCommandFailure)
    {
    }

    public bool DellAutomaticModeRestored => true;
}
