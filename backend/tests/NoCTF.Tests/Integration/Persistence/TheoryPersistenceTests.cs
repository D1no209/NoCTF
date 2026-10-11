using Microsoft.EntityFrameworkCore;
using FluentStorage.Storage;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using NoCTF.Tests.Fixtures.PostgreSql;
using NoCTF.Worker;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TheoryPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Generated_migration_persists_independent_theory_graph_and_enforces_receipts_and_immutability(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture();
            await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.ConnectionString, provider => provider.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            await Assert.That(await db.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), UserName = "theory-synthetic", Email = "theory@example.invalid",
                PasswordHash = "not-a-login", Role = UserRole.Organizer, CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user);
            var source = new TheoryQuestionSource { Id = Guid.NewGuid() };
            db.Add(source);
            var question = new TheoryQuestion { Id = Guid.NewGuid(), SourceId = source.Id, OwnerId = user.Id, CurrentVersion = 1,
                State = TheoryQuestionState.Available, CreatedAt = now, UpdatedAt = now };
            var revision = new TheoryQuestionRevision { Id = Guid.NewGuid(), QuestionId = question.Id, Version = 1,
                Title = "Synthetic question", Body = "Choose one", CreatedAt = now,
                Options = [new() { Id = Guid.NewGuid(), Order = 0, Content = "First", IsCorrect = true },
                    new() { Id = Guid.NewGuid(), Order = 1, Content = "Second" }] };
            question.Revisions.Add(revision);
            var file = new NoCTF.Domain.Storage.StoredFile { Id = Guid.NewGuid(), ObjectKey = "theory/synthetic.png",
                FileName = "synthetic.png", ContentType = "image/png", Sha256 = new byte[32], CreatedAt = now };
            db.Files.Add(file);
            revision.Files.Add(new() { FileId = file.Id });
            db.TheoryQuestions.Add(question);
            var rule = new TheoryDrawRule { Id = Guid.NewGuid(), Count = 1, Points = 10,
                Candidates = [new() { RevisionId = revision.Id, SourceId = source.Id }] };
            var activity = new TheoryActivity { Id = Guid.NewGuid(), OwnerId = user.Id, Kind = TheoryActivityKind.Practice,
                Name = "Synthetic practice", CreatedAt = now, OpensAt = now, ClosesAt = now.AddHours(1), DurationSeconds = 1800,
                Rules = [rule] };
            db.TheoryActivities.Add(activity);
            var participant = new TheoryParticipant { Id = Guid.NewGuid(), ActivityId = activity.Id, UserId = user.Id };
            db.TheoryParticipants.Add(participant);
            var item = new TheoryPaperItem { Id = Guid.NewGuid(), RevisionId = revision.Id, RuleId = rule.Id, Points = 10,
                Options = [new() { OptionId = revision.Options[1].Id, Order = 0 }, new() { OptionId = revision.Options[0].Id, Order = 1 }] };
            var paper = new TheoryPaper { Id = Guid.NewGuid(), ParticipantId = participant.Id, Attempt = 1,
                OpenedAt = now, Items = [item] };
            db.TheoryPapers.Add(paper);
            db.Add(new TheoryActivePaper { ParticipantId = participant.Id, PaperId = paper.Id });
            db.Add(new TheoryDrawReceipt { ParticipantId = participant.Id, PaperId = paper.Id, SourceId = source.Id, IssuedAt = now });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            var loaded = await db.TheoryPapers.Include(value => value.Items).ThenInclude(value => value.Options).SingleAsync(ct);
            await Assert.That(loaded.Items.Single().Options.OrderBy(value => value.Order).First().OptionId).IsEqualTo(revision.Options[1].Id);
            await Assert.That(await db.Challenges.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(0);
            var objects = Substitute.For<IStore>();
            await BackendMessageOperations.CleanupFileAsync(new CleanupFile(file.Id), db, objects, ct);
            await Assert.That(objects.ReceivedCalls().Count()).IsEqualTo(0);
            await Assert.That(await db.Files.AnyAsync(value => value.Id == file.Id, ct)).IsTrue();

            db.Add(new TheoryDrawReceipt { ParticipantId = participant.Id, PaperId = paper.Id, SourceId = source.Id, IssuedAt = now });
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<DbUpdateException>();
            db.ChangeTracker.Clear();

            var content = await db.TheoryQuestionRevisions.SingleAsync(ct);
            content.Body = "must not mutate an issued version";
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();

            var originalOption = await db.Set<TheoryQuestionOption>().FirstAsync(ct);
            originalOption.IsCorrect = !originalOption.IsCorrect;
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();

            db.Add(new TheoryQuestionOption { Id = Guid.NewGuid(), RevisionId = revision.Id, Order = 2, Content = "late option" });
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();

            var canonicalQuestion = await db.TheoryQuestions.SingleAsync(ct);
            canonicalQuestion.SourceId = Guid.NewGuid();
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();

            // A rolled-back new attempt leaves no paper or consumed identity behind.
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                db.TheoryPapers.Add(new() { Id = Guid.NewGuid(), ParticipantId = participant.Id, Attempt = 2, OpenedAt = now });
                await db.SaveChangesAsync(ct);
                await transaction.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
            await Assert.That(await db.TheoryPapers.CountAsync(ct)).IsEqualTo(1);

            await using var parallelDb = new NoCtfDbContext(options);
            var first = await db.TheoryPapers.SingleAsync(ct);
            var second = await parallelDb.TheoryPapers.SingleAsync(ct);
            first.SubmittedById = user.Id;
            second.SubmittedById = user.Id;
            await db.SaveChangesAsync(ct);
            await Assert.That(async () => await parallelDb.SaveChangesAsync(ct)).Throws<DbUpdateConcurrencyException>();

            db.ChangeTracker.Clear();
            db.TheoryParticipants.Add(new() { Id = Guid.NewGuid(), ActivityId = activity.Id, Kind = TheoryParticipantKind.Team,
                UserId = user.Id });
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
}
