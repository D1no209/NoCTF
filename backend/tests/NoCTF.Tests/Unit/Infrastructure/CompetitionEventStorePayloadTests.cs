using System.Text.Json;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Competitions.Events;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CompetitionEventStorePayloadTests
{
    [Test]
    public async Task Custom_payload_preserves_its_fields_and_adds_standard_fact_result()
    {
        var ids = new EventIds();
        var custom = AwdpFixResolvedEventPayload.Create(
            ids.GameplayFactId,
            ids.PatchUploadId,
            ids.RuntimeInstanceId,
            ids.TeamId,
            ids.CompetitionChallengeId,
            AwdpFixOutcome.DefenseSucceeded,
            null,
            ids.OccurredAt).Serialize();
        var json = CompetitionEventStore.SerializePayload(new(
            ids.CompetitionId,
            CompetitionEventKind.AwdpFixResolved,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            ids.OccurredAt,
            TeamId: ids.TeamId,
            CompetitionChallengeId: ids.CompetitionChallengeId,
            RuntimeInstanceId: ids.RuntimeInstanceId,
            GameplayFactId: ids.GameplayFactId,
            GameplayFactKind: GameplayFactKind.FixAttempt,
            GameplayFactState: GameplayFactState.Completed,
            GameplayFactResult: GameplayFactResult.Correct,
            PayloadJson: custom));

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("outcome").GetString())
            .IsEqualTo("DefenseSucceeded");
        await Assert.That(document.RootElement.GetProperty("gameplayFactState").GetString())
            .IsEqualTo("Completed");
        await Assert.That(document.RootElement.GetProperty("gameplayFactResult").GetString())
            .IsEqualTo("Correct");
    }

    [Test]
    public async Task Audience_change_payload_preserves_typed_modes_and_reason()
    {
        var ids = new EventIds();
        var json = CompetitionEventStore.SerializePayload(new(
            ids.CompetitionId,
            CompetitionEventKind.CompetitionAudienceChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            ids.OccurredAt,
            CompetitionAccessMode: CompetitionAccessMode.StaffOnly,
            PreviousCompetitionAccessMode: CompetitionAccessMode.Public,
            CompetitionAudienceChangeKind: CompetitionAudienceChangeKind.AccessMode));

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement
                .GetProperty("competitionAccessMode").GetString())
            .IsEqualTo("StaffOnly");
        await Assert.That(document.RootElement
                .GetProperty("previousCompetitionAccessMode").GetString())
            .IsEqualTo("Public");
        await Assert.That(document.RootElement
                .GetProperty("competitionAudienceChangeKind").GetString())
            .IsEqualTo("AccessMode");
    }

    private sealed class EventIds
    {
        public Guid CompetitionId { get; } = Guid.CreateVersion7();
        public Guid CompetitionChallengeId { get; } = Guid.CreateVersion7();
        public Guid TeamId { get; } = Guid.CreateVersion7();
        public Guid GameplayFactId { get; } = Guid.CreateVersion7();
        public Guid PatchUploadId { get; } = Guid.CreateVersion7();
        public Guid RuntimeInstanceId { get; } = Guid.CreateVersion7();
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }
}
