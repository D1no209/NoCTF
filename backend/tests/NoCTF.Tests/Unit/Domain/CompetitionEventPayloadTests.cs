using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;

namespace NoCTF.Tests.Unit.Domain;

public sealed class CompetitionEventPayloadTests
{
    [Test]
    public async Task Unknown_legacy_enum_values_are_ignored_without_breaking_audit_projection()
    {
        var competitionEvent = new CompetitionEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            Kind = CompetitionEventKind.LeaderboardVisibilityChanged,
            Level = CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Staff,
            SubjectType = EntityReferenceKind.Competition,
            SubjectId = Guid.NewGuid(),
            PayloadJson = """{"schemaVersion":1,"from":4,"leaderboardVisibility":2}""",
            OccurredAt = DateTimeOffset.UtcNow
        };

        await Assert.That(competitionEvent.PreviousLeaderboardVisibility).IsNull();
        await Assert.That(competitionEvent.LeaderboardVisibility)
            .IsEqualTo(CompetitionLeaderboardVisibility.Blackout);
    }
}
