using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Images;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionStartGateImageTests
{
    [Test]
    public async Task Published_challenge_with_a_tag_is_rejected_by_the_start_gate()
    {
        var challengeId = Guid.CreateVersion7();
        var gate = CreateGate(challengeId, "registry.example/acme/app:latest");

        var errors = await gate.ValidateAsync(Guid.CreateVersion7());

        var error = errors!.Single(item =>
            item.Code == StartGateFailureCode.RuntimeImageNotPinned);
        await Assert.That(error.CompetitionChallengeId).IsEqualTo(challengeId);
    }

    [Test]
    public async Task Published_challenge_with_a_digest_passes_the_image_gate()
    {
        var gate = CreateGate(
            Guid.CreateVersion7(),
            "registry.example/acme/app@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var errors = await gate.ValidateAsync(Guid.CreateVersion7());

        await Assert.That(errors!.Any(item =>
            item.Code == StartGateFailureCode.RuntimeImageNotPinned)).IsFalse();
    }

    private static CompetitionStartGate CreateGate(
        Guid challengeId,
        string image) =>
        new(
            new Store(new(
                Guid.CreateVersion7(),
                GameMode.Ctf,
                CompetitionStatus.Published,
                "{}",
                [new(challengeId, GameMode.Ctf, "{}", image, true)],
                ApprovedTeamCount: 1,
                MaxConcurrentRuntimeInstancesPerTeam: 1)),
            new CompetitionValidator(),
            new ChallengeValidator(),
            new Images());

    private sealed class Store(CompetitionStartGateSnapshot snapshot)
        : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(snapshot);
    }

    private sealed class CompetitionValidator : ICompetitionConfigurationValidator
    {
        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            int eligibleTeamCount,
            IReadOnlyList<string> challengeConfigurationJsons) => [];
    }

    private sealed class ChallengeValidator : IChallengeConfigurationCatalog
    {
        public string GetDefaultJson(GameMode mode) => "{}";

        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            string competitionConfigurationJson,
            int eligibleTeamCount) => [];
    }

    private sealed class Images : IChallengeImageDefinitionCatalog
    {
        public ChallengeImageDefinitionReadResult Read(
            GameMode mode,
            string definitionJson) =>
            new([new(
                new(ChallengeImageLocationKind.RuntimeContainer),
                definitionJson)]);

        public string Replace(
            GameMode mode,
            string definitionJson,
            IReadOnlyDictionary<ChallengeImageLocation, string> replacements) =>
            throw new NotSupportedException();
    }
}
