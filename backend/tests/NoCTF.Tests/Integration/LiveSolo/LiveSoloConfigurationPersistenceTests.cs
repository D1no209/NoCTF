using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.GameModes.Registration;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloConfigurationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Stage_only_changes_replace_and_clear_ordered_relations_without_changing_existing_match_targets(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await f.PrepareAsync(ct);
            var messages = Substitute.For<IPostCommitMessagePublisher>(); var store = new CompetitionConfigurationStore(f.Db, messages, NullCompetitionEventRecorder.Instance);
            var read = new GetCompetitionConfiguration(store);
            var management = new ManageLiveSoloConfiguration(read, new UpdateCompetitionConfiguration(store, new GameModeCompetitionConfigurationValidator()), new CompetitionModerationAuthorizer(f.Db));
            var current = (LiveSoloCompetitionModeConfiguration)(await read.ExecuteAsync(f.Competition.Id, ct))!.Configuration;
            current.StageRules = [new() { CompetitionId = f.Competition.Id, Lane = LiveSoloBracketLane.Winners, Stage = 1, RequiredWins = 3 }];
            var result = await management.SaveAsync(f.Competition.Id, f.Owner.Id, current, f.Now, ct);
            await Assert.That(result.Failure).IsNull();
            await using (var fresh = new NoCtfDbContext(f.Options))
                await Assert.That((await fresh.Set<LiveSoloStageRule>().SingleAsync(ct)).RequiredWins).IsEqualTo(3);
            current = (LiveSoloCompetitionModeConfiguration)(await read.ExecuteAsync(f.Competition.Id, ct))!.Configuration;
            current.StageRules[0].RequiredWins = 4;
            await Assert.That((await management.SaveAsync(f.Competition.Id, f.Owner.Id, current, f.Now, ct)).Failure).IsNull();
            await Assert.That((await f.Db.Set<LiveSoloStageRule>().SingleAsync(ct)).RequiredWins).IsEqualTo(4);
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).RequiredWins).IsEqualTo(1);
            current = (LiveSoloCompetitionModeConfiguration)(await read.ExecuteAsync(f.Competition.Id, ct))!.Configuration;
            current.StageRules.Clear(); await management.SaveAsync(f.Competition.Id, f.Owner.Id, current, f.Now, ct);
            await Assert.That(await f.Db.Set<LiveSoloStageRule>().CountAsync(ct)).IsEqualTo(0);
            current.BracketFormat = LiveSoloBracketFormat.DoubleElimination;
            await Assert.That((await management.SaveAsync(f.Competition.Id, f.Owner.Id, current, f.Now, ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
        });
    }
    [Test, Timeout(300_000)]
    public async Task An_unrelated_participant_cannot_write_and_invalid_policy_does_not_persist(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var store = new CompetitionConfigurationStore(f.Db, Substitute.For<IPostCommitMessagePublisher>(), NullCompetitionEventRecorder.Instance);
            var read = new GetCompetitionConfiguration(store);
            var management = new ManageLiveSoloConfiguration(read, new UpdateCompetitionConfiguration(store, new GameModeCompetitionConfigurationValidator()), new CompetitionModerationAuthorizer(f.Db));
            var current = (LiveSoloCompetitionModeConfiguration)(await read.ExecuteAsync(f.Competition.Id, ct))!.Configuration;
            await Assert.That((await management.SaveAsync(f.Competition.Id, f.Left.Id, current, f.Now, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            current.CountdownSeconds = 0;
            await Assert.That((await management.SaveAsync(f.Competition.Id, f.Owner.Id, current, f.Now, ct)).Failure).IsEqualTo(LiveSoloFailure.InvalidConfiguration);
            await Assert.That((await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).CountdownSeconds).IsEqualTo(5);
        });
    }
}
