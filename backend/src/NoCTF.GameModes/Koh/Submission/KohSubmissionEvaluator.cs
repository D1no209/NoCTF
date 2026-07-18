using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Koh.Submission;

public sealed class KohSubmissionEvaluator : IGameModeSubmissionEvaluator
{
    public GameMode Mode => GameMode.Koh;
}
