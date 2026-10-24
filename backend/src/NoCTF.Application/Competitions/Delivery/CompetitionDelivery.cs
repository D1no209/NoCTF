using NoCTF.Application.Runtime.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Competitions.Delivery;

public sealed record CompetitionDeliveryQuery(Guid CompetitionId, Guid? CompetitionChallengeId, Guid? TeamId,
    bool IncludeHistory, int Offset, int Limit, bool Desc, DateTimeOffset Now);
public sealed record CompetitionDeliveryAttachment(Guid Id, string FileName, bool Deleted);
public sealed record CompetitionDeliveryFlag(Guid Id, string Flag, ChallengeFlagMatchKind MatchKind,
    RuntimeFlagState State, Guid? AttachmentId, Guid? RuntimeInstanceId, DateTimeOffset CreatedAt,
    DateTimeOffset? ValidStart, DateTimeOffset? ValidUntil);
public sealed record CompetitionDeliveryItem(Guid TeamId, string TeamName, Guid CompetitionChallengeId,
    string ChallengeTitle, bool RandomAttachment, IReadOnlyList<CompetitionDeliveryAttachment> Attachments,
    IReadOnlyList<CompetitionDeliveryFlag> Flags);
public sealed record CompetitionDeliveryPage(IReadOnlyList<CompetitionDeliveryItem> Items, int Total);

public interface ICompetitionDeliveryReader
{
    Task<CompetitionDeliveryPage?> ReadAsync(CompetitionDeliveryQuery query, CancellationToken ct);
}
public sealed class QueryCompetitionDelivery(ICompetitionDeliveryReader reader)
{
    public Task<CompetitionDeliveryPage?> ExecuteAsync(CompetitionDeliveryQuery query, CancellationToken ct) => reader.ReadAsync(query, ct);
}
