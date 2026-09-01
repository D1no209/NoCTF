using System.Text.Json;
using NoCTF.Application.Competitions.Events;
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
    public async Task Legacy_success_payload_recovers_the_verified_result_for_existing_events()
    {
        var ids = new EventIds();
        var item = StoredEvent(ids, AwdpFixOutcome.DefenseSucceeded);

        var payload = CompetitionEventStore.ParsePayload(item);

        await Assert.That(payload.GameplayFactKind).IsEqualTo(GameplayFactKind.FixAttempt);
        await Assert.That(payload.GameplayFactState).IsEqualTo(GameplayFactState.Completed);
        await Assert.That(payload.GameplayFactResult).IsEqualTo(GameplayFactResult.Correct);
    }

    [Test]
    public async Task Legacy_platform_failure_remains_distinct_from_a_verified_failure()
    {
        var ids = new EventIds();
        var item = StoredEvent(ids, AwdpFixOutcome.PlatformFailed);

        var payload = CompetitionEventStore.ParsePayload(item);

        await Assert.That(payload.GameplayFactKind).IsEqualTo(GameplayFactKind.FixAttempt);
        await Assert.That(payload.GameplayFactState).IsEqualTo(GameplayFactState.PlatformFailed);
        await Assert.That(payload.GameplayFactResult).IsNull();
    }

    private static CompetitionEvent StoredEvent(EventIds ids, AwdpFixOutcome outcome) => new()
    {
        Id = Guid.CreateVersion7(),
        CompetitionId = ids.CompetitionId,
        Kind = CompetitionEventKind.AwdpFixResolved,
        Level = CompetitionEventLevel.Information,
        Visibility = CompetitionEventVisibility.Public,
        SubjectType = EntityReferenceKind.GameplayFact,
        SubjectId = ids.GameplayFactId,
        PayloadJson = AwdpFixResolvedEventPayload.Create(
            ids.GameplayFactId,
            ids.PatchUploadId,
            ids.RuntimeInstanceId,
            ids.TeamId,
            ids.CompetitionChallengeId,
            outcome,
            outcome == AwdpFixOutcome.PlatformFailed
                ? GameplayFactFailureCode.AwdpPlatformFailed
                : null,
            ids.OccurredAt).Serialize(),
        OccurredAt = ids.OccurredAt
    };

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
