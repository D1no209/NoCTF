namespace NoCTF.Domain.Submissions;

public enum SubmissionKind
{
    Flag,
    Break,
    Fix,
    HintUnlock,
    ManualAdjust
}

public enum EvaluationDispatchMode : short
{
    Automatic,
    ManualBatch
}

public enum SubmissionEvaluationState : short
{
    Pending,
    Queued,
    Processing,
    Completed,
    PlatformFailed
}
