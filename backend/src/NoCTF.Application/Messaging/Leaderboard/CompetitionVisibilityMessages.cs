namespace NoCTF.Application.Messaging;

public sealed record ApplyCompetitionVisibility(
    Guid CompetitionId,
    int VisibilityRevision);
