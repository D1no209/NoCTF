namespace NoCTF.PluginBase;

public enum GameModeType { Ctf, Awd, Awdp, Koh }

public enum SubmissionResult
{
    Accepted,
    AlreadySolved,
    WrongFlag,
    InvalidFormat,
    CompetitionNotStarted,
    CompetitionEnded,
    InstanceRequired,
    InstanceExpired,
    AttemptsExhausted,
    FlagRateLimited,
    NotImplemented
}

public enum ValidationResult
{
    Valid,
    Invalid,
    Expired,
    AlreadyUsed,
    NotYetAvailable
}
