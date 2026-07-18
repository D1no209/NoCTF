namespace NoCTF.GameModes.Koh.Scoring;

public static class KohScoringRules
{
    public static Guid? ControllerForInterval(Guid? lastController, Guid? observedController, bool authoritative) =>
        authoritative ? observedController : lastController;
}
