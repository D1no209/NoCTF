namespace NoCTF.Domain.Runtime;

public enum AwdServiceState : short
{
    Unknown,
    Up,
    Down,
    CheckerAbnormalExit,
    CheckerTimedOut
}
