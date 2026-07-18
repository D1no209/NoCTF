using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Penetration.Submission;

public sealed class PenetrationSubmissionEvaluator : IGameModeSubmissionEvaluator
{
    public GameMode Mode => GameMode.Penetration;
}
