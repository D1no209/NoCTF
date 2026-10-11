using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;
using Npgsql;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloCompetitionDeletionTests
{
    [Test, Timeout(300_000)]
    public Task Complete_series_chain_deletes_in_dependency_order_and_preserves_other_competitions_and_bank_material(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var owned = new LiveSoloChallenge { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "Local material", Direction = "Web",
                Definition = new LiveSoloChallengeDefinition(), CatalogScope = ChallengeCatalogScope.CompetitionOwned, CreatedAt = f.Now, UpdatedAt = f.Now };
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = Guid.NewGuid().ToString("N"), FileName = "answer.txt", ContentType = "text/plain",
                Sha256 = new byte[32], ByteLength = 4, CreatedAt = f.Now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = owned.Id, FileId = file.Id, CreatedAt = f.Now };
            f.Db.Add(owned); f.Db.Files.Add(file); f.Db.Add(attachment);
            f.Db.Add(new LiveSoloChallengeSource { ChallengeId = owned.Id, OriginalChallengeId = f.Templates[0].Id, CanonicalChallengeId = f.Templates[0].Id, CopiedAt = f.Now });
            f.Db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = owned.Id, Flag = "flag{first}", FlagSha256 = ManageChallengeFlags.Hash("flag{first}"), CreatedAt = f.Now });
            f.Entries[0].ChallengeId = owned.Id;
            f.Db.Add(new LiveSoloQuestionMaterial { CompetitionChallengeId = f.Entries[0].Id, CompetitionId = f.Competition.Id, ChallengeId = owned.Id,
                SourceTemplateId = f.Templates[0].Id, CreatedByUserId = f.Owner.Id, CreatedAt = f.Now });
            await f.Db.SaveChangesAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            var q = f.Round.Questions.Single(x => x.Position == 0);
            var attempt = await f.Store(f.Db).AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, q.Id, f.Left.Id, "flag{first}", f.Now), ct);
            await f.Store(f.Db).ResolveAsync(f.Round.Id, f.Now, ct);
            await Assert.That(f.Round.WinningGameplayFactId).IsEqualTo(attempt.GameplayFactId);
            // This is the exact FK that blocked the production request; the failed statement must roll back.
            await using (var transaction = await f.Db.Database.BeginTransactionAsync(ct))
            {
                var error = await Assert.That(async () => await f.Db.GameplayFacts.Where(x => x.CompetitionId == f.Competition.Id).ExecuteDeleteAsync(ct))
                    .Throws<PostgresException>();
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
                await transaction.RollbackAsync(ct);
            }
            var media = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            var capture = await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            var frame = new LiveSoloProgramFrame { Id = Guid.NewGuid(), MediaSessionId = media.Id, OccurredAt = f.Now.AddSeconds(1),
                Questions = [new LiveSoloProgramFrameQuestion { RoundQuestionId = q.Id, CompetitionChallengeId = q.CompetitionChallengeId,
                    CanonicalChallengeId = f.Templates[0].Id, Title = "Opened question", OpenedAt = f.Now }] };
            media.State = LiveSoloMediaState.Stopped; media.StoppedAt = f.Now;
            capture.State = LiveSoloCaptureState.Completed; capture.RawRemovedAt = f.Now;
            var recording = new LiveSoloRecording { Id = Guid.NewGuid(), MediaSessionId = media.Id, RoundId = f.Round.Id, UserId = f.Left.Id,
                State = LiveSoloRecordingState.Completed, EgressId = "synthetic-finished-export", RawRemovedAt = f.Now, FileId = file.Id,
                CreatedAt = f.Now, KeepUntil = f.Now.AddDays(1) };
            f.Db.AddRange(recording, frame,
                new LiveSoloMediaGrant { Id = Guid.NewGuid(), MediaSessionId = media.Id, UserId = f.Left.Id, Identity = "synthetic", IssuedAt = f.Now },
                new LiveSoloViewerLease { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, MatchId = f.Match.Id, UserId = f.Left.Id, ExpiresAt = f.Now },
                new LiveSoloProgramDecision { Id = Guid.NewGuid(), MediaSessionId = media.Id, ProgramCaptureId = capture.Id, ActorUserId = f.Owner.Id, Reason = "Synthetic decision", OccurredAt = f.Now },
                new LiveSoloRecordingDecision { Id = Guid.NewGuid(), MediaSessionId = media.Id, RecordingId = recording.Id, ActorUserId = f.Owner.Id, Reason = "Synthetic decision", OccurredAt = f.Now },
                new LiveSoloAdjudication { Id = Guid.NewGuid(), MatchId = f.Match.Id, RoundId = f.Round.Id, ActorUserId = f.Owner.Id, Reason = "Synthetic decision", OccurredAt = f.Now },
                new LiveSoloAttachmentAssignment { RoundQuestionId = q.Id, TeamId = f.LeftTeam.Id, AttachmentId = attachment.Id, AssignedAt = f.Now });
            var download = new AttachmentDownloadGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = q.CompetitionChallengeId,
                TeamId = f.LeftTeam.Id, ActorUserId = f.Left.Id, ReferenceKind = GameplayFactReferenceKind.Attachment, ReferenceId = attachment.Id,
                State = GameplayFactState.Completed, Result = GameplayFactResult.Applied, OccurredAt = f.Now, UpdatedAt = f.Now };
            f.Db.Add(download); f.Db.Add(new LiveSoloDownloadEvidence { GameplayFactId = download.Id, RoundQuestionId = q.Id });
            var runtime = new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = q.CompetitionChallengeId,
                TeamId = f.LeftTeam.Id, ExecutionScopeId = q.Id, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Stopped, CreatedAt = f.Now, StoppedAt = f.Now };
            f.Db.Add(runtime); f.Db.Add(new LiveSoloRuntimeBinding { RoundQuestionId = q.Id, Side = LiveSoloSide.Left, RuntimeInstanceId = runtime.Id });
            var downstream = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Position = 1, CreatedAt = f.Now,
                Slots = [new LiveSoloMatchSlot { Side = LiveSoloSide.Left, Source = LiveSoloSlotSource.Winner, SourceMatchId = f.Match.Id }] };
            f.Db.Add(downstream);
            var correction = new LiveSoloResultCorrection { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, MatchId = f.Match.Id, ActorUserId = f.Owner.Id,
                PreviousWinnerTeamId = f.LeftTeam.Id, WinnerTeamId = f.RightTeam.Id, Reason = "Synthetic correction", CreatedAt = f.Now,
                Matches = [new LiveSoloCorrectionMatch { MatchId = downstream.Id, ReplacementMatchId = downstream.Id }] };
            f.Db.Add(correction);
            var match = await f.Db.LiveSoloMatches.SingleAsync(x => x.Id == f.Match.Id, ct);
            match.PendingCorrectionId = correction.Id; match.ReplacementMatchId = downstream.Id;
            var other = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "Preserved competition", StartAt = f.Now, EndAt = f.Now.AddHours(1),
                CreatedAt = f.Now, UpdatedAt = f.Now, FlagDerivationSecret = new byte[32], ModeConfiguration = new LiveSoloCompetitionModeConfiguration() };
            f.Db.Add(other);
            f.Db.Add(new LiveSoloCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = other.Id, ChallengeId = f.Templates[0].Id, UpdatedAt = f.Now, Rules = new LiveSoloCompetitionChallengeRules() });
            var otherMatch = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = other.Id, CreatedAt = f.Now };
            f.Db.Add(otherMatch);
            f.Db.Add(new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = f.Templates[0].Id, FileId = file.Id, CreatedAt = f.Now });
            f.Competition.Status = CompetitionStatus.Finished;
            await f.Db.SaveChangesAsync(ct);
            var files = await f.Db.Files.Select(x => x.Id).ToArrayAsync(ct);
            f.Db.ChangeTracker.Clear();
            var messages = Substitute.For<IPostCommitMessagePublisher>();
            var store = new AdminCompetitionStore(f.Db, messageOutbox: messages);
            var preview = await store.PreviewHardDeleteAsync(f.Competition.Id, f.Owner.Id, true, ct);
            await Assert.That(preview!.References.Single(x => x.Kind == CompetitionHardDeleteReferenceKind.LiveSoloMatch).Count).IsEqualTo(2);
            await Assert.That(preview.CanForceDelete).IsTrue();
            var deleted = await store.ForceDeleteAsync(Command(f), true, ct);
            await Assert.That(deleted.State).IsEqualTo(CompetitionForceDeleteState.Deleted);
            await Assert.That((await f.Db.Competitions.SingleAsync(ct)).Id).IsEqualTo(other.Id);
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).Id).IsEqualTo(otherMatch.Id);
            await Assert.That(await f.Db.LiveSoloRounds.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloQuestionGroups.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloMediaSessions.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.Set<LiveSoloOpenedContent>().CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.Set<LiveSoloQuestionMaterial>().CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.Challenges.AnyAsync(x => x.Id == owned.Id, ct)).IsFalse();
            await Assert.That(await f.Db.Challenges.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await f.Db.ChallengeFlags.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await f.Db.Files.Select(x => x.Id).ToArrayAsync(ct)).IsEquivalentTo(files);
            await Assert.That(await f.Db.Notifications.CountAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct)).IsEqualTo(1);
            foreach (var id in files) await messages.Received(1).PublishAsync(Arg.Is<CleanupFile>(x => x != null && x.FileId == id));
        });

    [Test, Timeout(300_000)]
    public Task Deleting_a_historical_snapshot_queues_its_file_even_when_current_attachment_was_removed(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = Guid.NewGuid().ToString("N"), FileName = "old.txt", ContentType = "text/plain", Sha256 = new byte[32], CreatedAt = f.Now };
            var attachment = new ChallengeAttachment { Id = Guid.NewGuid(), ChallengeId = f.Templates[0].Id, FileId = file.Id, CreatedAt = f.Now };
            f.Db.AddRange(file, attachment); await f.Db.SaveChangesAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            await f.Db.Set<ChallengeAttachment>().Where(x => x.Id == attachment.Id).ExecuteDeleteAsync(ct);
            await FinishAsync(f, ct);
            var messages = Substitute.For<IPostCommitMessagePublisher>();
            await Assert.That((await new AdminCompetitionStore(f.Db, messageOutbox: messages).ForceDeleteAsync(Command(f), true, ct)).State).IsEqualTo(CompetitionForceDeleteState.Deleted);
            await messages.Received(1).PublishAsync(Arg.Is<CleanupFile>(x => x != null && x.FileId == file.Id));
        });

    [Test, Timeout(300_000)]
    public Task A_final_delete_failure_rolls_back_rounds_material_snapshots_and_cyclic_media_pointers(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            session.State = LiveSoloMediaState.Stopped;
            var capture = await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            capture.State = LiveSoloCaptureState.Completed; capture.RawRemovedAt = f.Now;
            await f.Db.SaveChangesAsync(ct); await FinishAsync(f, ct);
            await f.Db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_solo_test_delete() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Injected Solo deletion failure'; END $$;
                CREATE TRIGGER reject_solo_delete BEFORE DELETE ON competitions
                FOR EACH ROW EXECUTE FUNCTION reject_solo_test_delete();
                """, ct);
            await Assert.That(async () => await new AdminCompetitionStore(f.Db).ForceDeleteAsync(Command(f), true, ct)).Throws<PostgresException>();
            f.Db.ChangeTracker.Clear();
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).CurrentRoundId).IsEqualTo(f.Round.Id);
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).CurrentMediaSessionId).IsEqualTo(session.Id);
            await Assert.That((await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).CurrentProgramCaptureId).IsEqualTo(capture.Id);
            await Assert.That(await f.Db.Set<LiveSoloOpenedContent>().CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(ct)).IsGreaterThan(0);
            await Assert.That(await f.Db.LiveSoloQuestionGroups.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await f.Db.Notifications.AnyAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct)).IsFalse();
        });

    [Test, Timeout(300_000)]
    [Arguments(0)] [Arguments(1)] [Arguments(2)] [Arguments(3)]
    public Task Media_and_unremoved_raw_exports_block_deletion_without_losing_history(int resource, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await f.PrepareAsync(ct);
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            var capture = await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            session.State = resource == 0 ? LiveSoloMediaState.Ready : LiveSoloMediaState.Stopped;
            capture.State = resource == 1 ? LiveSoloCaptureState.Active : LiveSoloCaptureState.Completed;
            capture.RawRemovedAt = resource is 0 or 3 ? f.Now : null;
            if (resource == 3) f.Db.LiveSoloRecordings.Add(new LiveSoloRecording { Id = Guid.NewGuid(), MediaSessionId = session.Id, UserId = f.Left.Id,
                State = LiveSoloRecordingState.Completed, EgressId = "unremoved-raw-export", CreatedAt = f.Now, KeepUntil = f.Now.AddDays(1) });
            await f.Db.SaveChangesAsync(ct); await FinishAsync(f, ct);
            var store = new AdminCompetitionStore(f.Db);
            await Assert.That((await store.PreviewHardDeleteAsync(f.Competition.Id, f.Owner.Id, true, ct))!.CanForceDelete).IsFalse();
            await Assert.That((await store.ForceDeleteAsync(Command(f), true, ct)).State).IsEqualTo(CompetitionForceDeleteState.ActiveLiveSoloMediaResource);
            await Assert.That(await f.Db.LiveSoloRounds.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await f.Db.LiveSoloMediaSessions.CountAsync(ct)).IsEqualTo(1);
        });

    [Test, Timeout(300_000)]
    public Task A_question_list_without_other_history_blocks_unconfirmed_hard_delete_and_can_be_force_deleted(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Db.LiveSoloQuestionGroups.Add(new LiveSoloQuestionGroup { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Empty draft list" });
            await f.Db.SaveChangesAsync(ct);
            await f.Db.CompetitionChallenges.ExecuteDeleteAsync(ct); await f.Db.Teams.ExecuteDeleteAsync(ct); await FinishAsync(f, ct);
            var store = new AdminCompetitionStore(f.Db);
            var preview = (await store.PreviewHardDeleteAsync(f.Competition.Id, f.Owner.Id, true, ct))!;
            await Assert.That(preview.CanHardDelete).IsFalse();
            await Assert.That(preview.References.Single().Kind).IsEqualTo(CompetitionHardDeleteReferenceKind.LiveSoloQuestionGroup);
            await Assert.That((await store.HardDeleteAsync(f.Competition.Id, f.Owner.Id, true, ct)).State).IsEqualTo(CompetitionHardDeleteState.Blocked);
            await Assert.That((await store.ForceDeleteAsync(Command(f), true, ct)).State).IsEqualTo(CompetitionForceDeleteState.Deleted);
        });

    private static ForceDeleteCompetitionCommand Command(LiveSoloMatchPersistenceTests.Fixture f) =>
        new(f.Competition.Id, f.Owner.Id, f.Competition.Title, "Delete the synthetic test competition.", f.Now);

    private static async Task FinishAsync(LiveSoloMatchPersistenceTests.Fixture f, CancellationToken ct)
    {
        await f.Db.Competitions.Where(x => x.Id == f.Competition.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CompetitionStatus.Finished), ct);
        f.Db.ChangeTracker.Clear();
    }
}
