using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Submissions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Submissions;

[Mapper]
public static partial class SubmissionMapper
{
    public static partial AcceptedSubmissionResponse ToResponse(SubmissionAccepted accepted);

    [MapProperty(nameof(SubmissionStatusView.Result), nameof(SubmissionStatusResponse.State))]
    [MapperIgnoreSource(nameof(SubmissionStatusView.CompetitionId))]
    [MapperIgnoreSource(nameof(SubmissionStatusView.TeamId))]
    [MapperIgnoreSource(nameof(SubmissionStatusView.ChallengeId))]
    [MapperIgnoreSource(nameof(SubmissionStatusView.EvaluatorVersion))]
    public static partial SubmissionStatusResponse ToStatusResponse(SubmissionStatusView view);

    private static SubmissionStatusState MapState(ScoringResult? result) =>
        result is null
            ? SubmissionStatusState.Unprocessed
            : result == ScoringResult.Correct
                ? SubmissionStatusState.Success
                : SubmissionStatusState.Failure;

    [MapperIgnoreSource(nameof(AdminSubmissionStatusView.CompetitionId))]
    public static partial AdminSubmissionStatusResponse ToAdminStatusResponse(AdminSubmissionStatusView view);
}
