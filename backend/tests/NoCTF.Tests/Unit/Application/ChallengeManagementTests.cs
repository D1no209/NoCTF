using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class ChallengeManagementTests
{
    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task CreateChallenge_AllLifecycleStatesAllowMutation(CompetitionStatus status)
    {
        var store = new Store { Status = status };

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(CreateCommand());

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Challenge).IsNotNull();
        await Assert.That(store.CreateCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(-1L, 0, ChallengeMutationFailure.InvalidBaseScore)]
    [Arguments(100L, -1, ChallengeMutationFailure.InvalidOrder)]
    public async Task CreateChallenge_InvalidDefinition_ReturnsTypedFailure(
        long baseScore,
        int order,
        ChallengeMutationFailure expectedFailure)
    {
        var store = new Store();

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(
            CreateCommand() with { BaseScore = baseScore, Order = order });

        await Assert.That(result.Failure).IsEqualTo(expectedFailure);
        await Assert.That(result.Challenge).IsNull();
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(ChallengeMutationFailure.ResourceIdConflict)]
    [Arguments(ChallengeMutationFailure.ChallengeOrderConflict)]
    [Arguments(ChallengeMutationFailure.ChallengeTemplateConflict)]
    public async Task CreateChallenge_Conflict_PropagatesTypedFailure(
        ChallengeMutationFailure expectedFailure)
    {
        var store = new Store
        {
            CreateResult = new ChallengeMutationResult(null, expectedFailure)
        };

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(CreateCommand());

        await Assert.That(result.Failure).IsEqualTo(expectedFailure);
    }

    [Test]
    public async Task CreateChallenge_TemplateModeMismatch_PropagatesTypedFailure()
    {
        var store = new Store
        {
            CreateResult = new ChallengeMutationResult(
                null,
                ChallengeMutationFailure.TemplateModeMismatch)
        };

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(CreateCommand());

        await Assert.That(result.Failure)
            .IsEqualTo(ChallengeMutationFailure.TemplateModeMismatch);
    }

    [Test]
    public async Task ListChallenges_PublicQuery_ExcludesUnpublishedAtStoreBoundary()
    {
        var store = new Store();

        await new ListChallenges(store).ExecuteAsync(Guid.NewGuid(), includeUnpublished: false);

        await Assert.That(store.LastIncludeUnpublished).IsFalse();
    }

    [Test]
    public async Task GetChallenge_AdminQuery_IncludesUnpublishedAtStoreBoundary()
    {
        var store = new Store();

        await new GetChallenge(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), includeUnpublished: true);

        await Assert.That(store.LastIncludeUnpublished).IsTrue();
    }

    [Test]
    [Arguments(ChallengeMutation.Update)]
    [Arguments(ChallengeMutation.Delete)]
    [Arguments(ChallengeMutation.Restore)]
    public async Task Mutation_ActiveCompetition_IsAllowed(ChallengeMutation mutation)
    {
        var store = new Store { Status = CompetitionStatus.Running };

        var error = await ExecuteMutationAsync(mutation, store);

        await Assert.That(error).IsNull();
        await Assert.That(store.MutationCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(ChallengeMutation.Update)]
    [Arguments(ChallengeMutation.Delete)]
    [Arguments(ChallengeMutation.Restore)]
    public async Task Mutation_Succeeds_UsesExpectedRevision(ChallengeMutation mutation)
    {
        var competitionId = Guid.NewGuid();
        var store = new Store();

        var failure = await ExecuteMutationAsync(
            mutation,
            store,
            competitionId);

        await Assert.That(failure).IsNull();
        await Assert.That(store.LastExpectedRevision).IsEqualTo(0);
    }

    [Test]
    [Arguments(ChallengeMutation.Update)]
    [Arguments(ChallengeMutation.Delete)]
    [Arguments(ChallengeMutation.Restore)]
    public async Task Mutation_InvalidRevision_IsRejectedBeforeStoreOrSideEffects(
        ChallengeMutation mutation)
    {
        var store = new Store();

        var failure = await ExecuteMutationAsync(
            mutation,
            store,
            expectedRevision: -1);

        await Assert.That(failure).IsEqualTo(ChallengeMutationFailure.InvalidRevision);
        await Assert.That(store.MutationCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(ChallengeMutation.Update, ChallengeMutationFailure.CompetitionNotFound)]
    [Arguments(ChallengeMutation.Update, ChallengeMutationFailure.ChallengeNotFound)]
    [Arguments(ChallengeMutation.Update, ChallengeMutationFailure.ChallengeOrderConflict)]
    [Arguments(ChallengeMutation.Update, ChallengeMutationFailure.RevisionConflict)]
    [Arguments(ChallengeMutation.Delete, ChallengeMutationFailure.CompetitionNotFound)]
    [Arguments(ChallengeMutation.Delete, ChallengeMutationFailure.ChallengeNotFound)]
    [Arguments(ChallengeMutation.Delete, ChallengeMutationFailure.RevisionConflict)]
    [Arguments(ChallengeMutation.Delete, ChallengeMutationFailure.LifecycleStateConflict)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.CompetitionNotFound)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.TemplateNotFound)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.TemplateModeMismatch)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.ChallengeNotFound)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.ChallengeOrderConflict)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.ChallengeTemplateConflict)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.RevisionConflict)]
    [Arguments(ChallengeMutation.Restore, ChallengeMutationFailure.LifecycleStateConflict)]
    public async Task Mutation_TypedFailure_IsPropagated(
        ChallengeMutation mutation,
        ChallengeMutationFailure expectedFailure)
    {
        var store = new Store { MutationFailure = expectedFailure };

        var failure = await ExecuteMutationAsync(mutation, store);

        await Assert.That(failure).IsEqualTo(expectedFailure);
        await Assert.That(store.MutationCalls).IsEqualTo(1);
    }

    public enum ChallengeMutation
    {
        Update,
        Delete,
        Restore
    }

    private static async Task<ChallengeMutationFailure?> ExecuteMutationAsync(
        ChallengeMutation mutation,
        Store store,
        Guid? competitionId = null,
        int expectedRevision = 0)
    {
        var actualCompetitionId = competitionId ?? Guid.NewGuid();
        return mutation switch
        {
            ChallengeMutation.Update => (await new UpdateChallenge(store)
                .ExecuteAsync(UpdateCommand(actualCompetitionId) with
                {
                    ExpectedRevision = expectedRevision
                })).Failure,
            ChallengeMutation.Delete => await new DeleteChallenge(store)
                .ExecuteAsync(
                    actualCompetitionId,
                    Guid.NewGuid(),
                    expectedRevision,
                    DateTimeOffset.UtcNow),
            ChallengeMutation.Restore => await new DeleteChallenge(store)
                .RestoreAsync(
                    actualCompetitionId,
                    Guid.NewGuid(),
                    expectedRevision,
                    DateTimeOffset.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
    }

    private static CreateCompetitionChallengeCommand CreateCommand(Guid? competitionId = null) => new(
        null,
        competitionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        100,
        1,
        DateTimeOffset.UtcNow);

    private static UpdateCompetitionChallengeCommand UpdateCommand(Guid? competitionId = null) => new(
        competitionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        100,
        2,
        true,
        0,
        DateTimeOffset.UtcNow);

    private sealed class Store : IChallengeManagementStore
    {
        private readonly ChallengeView challenge = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Web 100",
            "Description",
            "Web",
            100,
            1,
            false,
            0,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        public CompetitionStatus? Status { get; init; } = CompetitionStatus.Draft;
        public ChallengeMutationResult? CreateResult { get; init; }
        public ChallengeMutationFailure? MutationFailure { get; init; }
        public int CreateCalls { get; private set; }
        public int MutationCalls { get; private set; }
        public int? LastExpectedRevision { get; private set; }
        public bool? LastIncludeUnpublished { get; private set; }

        public Task<ChallengeCompetitionContext?> GetCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(
                Status is null ? null : new ChallengeCompetitionContext(GameMode.Ctf, Status.Value));

        public Task<ChallengeMutationResult> CreateAsync(
            CreateCompetitionChallengeCommand command,
            string configurationJson,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(CreateResult ?? new ChallengeMutationResult(challenge, null));
        }

        public Task<ChallengeView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            bool includeUnpublished,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            LastIncludeUnpublished = includeUnpublished;
            return Task.FromResult<ChallengeView?>(challenge);
        }

        public Task<IReadOnlyList<ChallengeView>> ListAsync(
            Guid competitionId,
            bool includeUnpublished,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            LastIncludeUnpublished = includeUnpublished;
            return Task.FromResult<IReadOnlyList<ChallengeView>>([challenge]);
        }

        public Task<ChallengeMutationResult> UpdateAsync(
            UpdateCompetitionChallengeCommand command,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            LastExpectedRevision = command.ExpectedRevision;
            return Task.FromResult(MutationFailure is null
                ? new ChallengeMutationResult(challenge)
                : new ChallengeMutationResult(null, MutationFailure));
        }

        public Task<ChallengeMutationFailure?> SoftDeleteAsync(
            Guid competitionId,
            Guid challengeId,
            int expectedRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            LastExpectedRevision = expectedRevision;
            return Task.FromResult(MutationFailure);
        }

        public Task<ChallengeMutationFailure?> RestoreAsync(
            Guid competitionId,
            Guid challengeId,
            int expectedRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            LastExpectedRevision = expectedRevision;
            return Task.FromResult(MutationFailure);
        }
    }

    private sealed class Catalog : IChallengeConfigurationCatalog
    {
        public string GetDefaultJson(GameMode mode) => """{"schemaVersion":1}""";

        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            string competitionConfigurationJson,
            int eligibleTeamCount) => [];
    }

}
