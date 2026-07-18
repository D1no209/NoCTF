namespace NoCTF.Application.Submissions.Processing;

public sealed record ProcessFlagSubmission(Guid CompetitionId, Guid TeamId, Guid UserId, Guid SubmissionId);

public sealed record ProcessFixSubmission(Guid CompetitionId, Guid TeamId, Guid UserId, Guid SubmissionId);
