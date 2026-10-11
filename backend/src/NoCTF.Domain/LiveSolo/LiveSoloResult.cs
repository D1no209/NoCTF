namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloEndReason : short
{
    Unrecorded, CorrectAnswer, RequiredWinsReached, Forfeit, TimeLimit, Voided,
    EligibilityChanged, SelectionChanged, ResultCorrected, DownstreamReplaced, Bye
}
