using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class LiveSoloStartGateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Solo_start_ignores_publication_and_unused_pool_definition_checks(bool legacyPublished)
    {
        var snapshot = Snapshot(GameMode.LiveSolo) with
        {
            // Pool content is checked for the selected Round; legacy publication must not revive ordinary gates.
            Challenges = [new(Guid.NewGuid(), GameMode.Ctf, new CtfCompetitionChallengeRules(),
                new CtfChallengeDefinition(), legacyPublished, [-1])]
        };
        await Assert.That((await Gate(snapshot).ValidateAsync(snapshot.CompetitionId))!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Solo_competition_can_start_before_a_question_list_is_prepared()
    {
        var snapshot = Snapshot(GameMode.LiveSolo);
        await Assert.That((await Gate(snapshot).ValidateAsync(snapshot.CompetitionId))!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Solo_still_requires_competition_publication_valid_configuration_and_an_approved_team()
    {
        var snapshot = Snapshot(GameMode.LiveSolo) with
        {
            Status = CompetitionStatus.Draft, ApprovedTeamCount = 0,
            Configuration = new LiveSoloCompetitionModeConfiguration { RoundLimitSeconds = 0 }
        };
        var codes = (await Gate(snapshot).ValidateAsync(snapshot.CompetitionId))!.Select(x => x.Code).ToArray();
        await Assert.That(codes).Contains(StartGateFailureCode.CompetitionNotPublished);
        await Assert.That(codes).Contains(StartGateFailureCode.CompetitionConfigurationInvalid);
        await Assert.That(codes).Contains(StartGateFailureCode.ApprovedTeamRequired);
        await Assert.That(codes).DoesNotContain(StartGateFailureCode.PublishedChallengeRequired);
    }

    [Test]
    [Arguments(GameMode.Ctf)] [Arguments(GameMode.Awd)] [Arguments(GameMode.Awdp)] [Arguments(GameMode.Koh)]
    public async Task Ordinary_modes_keep_the_published_challenge_requirement(GameMode mode)
    {
        var snapshot = Snapshot(mode);
        await Assert.That((await Gate(snapshot).ValidateAsync(snapshot.CompetitionId))!.Select(x => x.Code))
            .Contains(StartGateFailureCode.PublishedChallengeRequired);
    }

    private static CompetitionStartGateSnapshot Snapshot(GameMode mode)
    {
        var id = Guid.NewGuid();
        return new(id, mode, CompetitionStatus.Published, CompetitionModeConfigurationDefaults.Create(mode, id), [], 2, 1,
            CompetitionTrackConfiguration.ToPersisted(CompetitionTrackConfiguration.DefaultFor(mode), id));
    }

    private static CompetitionStartGate Gate(CompetitionStartGateSnapshot snapshot) =>
        new(new Store(snapshot), new GameModeCompetitionConfigurationValidator(), new GameModeChallengeConfigurationCatalog());

    private sealed class Store(CompetitionStartGateSnapshot snapshot) : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(snapshot);
    }
}
