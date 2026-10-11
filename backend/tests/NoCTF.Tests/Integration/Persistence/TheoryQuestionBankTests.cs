using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Questions;
using NoCTF.Tests.Fixtures.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TheoryQuestionBankTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Bank_permissions_protect_answers_and_copies_keep_their_source_without_mutating_old_versions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture();
            await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString)
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = User("theory-owner", UserRole.Organizer, now);
            var consumer = User("theory-consumer", UserRole.User, now);
            var editor = User("theory-editor", UserRole.Organizer, now);
            db.Users.AddRange(owner, consumer, editor);
            await db.SaveChangesAsync(ct);
            var bank = new TheoryQuestionBank(db);
            var manage = new ManageTheoryQuestions(bank);
            var content = new TheoryQuestionInput("Synthetic one", "Choose one", "Private explanation", TheoryQuestionKind.SingleChoice,
                TheoryQuestionDifficulty.Basic, [new("first", true), new("second", false)],
                [new(TheoryClassificationKind.Knowledge, "Web")], []);
            var forbiddenCreate = await manage.SaveAsync(new(consumer.Id, null, content, now), ct);
            await Assert.That(forbiddenCreate.Failure).IsEqualTo(TheoryBankFailure.CreationForbidden);
            var created = await manage.SaveAsync(new(owner.Id, null, content, now), ct);
            await Assert.That(created.Failure).IsNull();
            var id = created.Saved!.QuestionId;
            await Assert.That(await bank.ReadAsync(consumer.Id, id, ct)).IsNull();
            await Assert.That(await manage.GrantAsync(owner.Id, id, consumer.Id, TheoryQuestionPermission.Use, ct)).IsNull();
            var listed = await bank.ListAsync(consumer.Id, "synthetic", TheoryQuestionKind.SingleChoice, 0, 10, ct);
            await Assert.That(listed.Total).IsEqualTo(1);
            await Assert.That(listed.Items.Single().CanReadAnswers).IsFalse();
            await Assert.That(listed.Items.Single().CanManage).IsFalse();
            await Assert.That(await bank.ReadAsync(consumer.Id, id, ct)).IsNull();
            await Assert.That((await manage.SaveAsync(new(consumer.Id, id, content, now), ct)).Failure)
                .IsEqualTo(TheoryBankFailure.NotFoundOrForbidden);
            await Assert.That(await manage.GrantAsync(consumer.Id, id, editor.Id, TheoryQuestionPermission.Manage, ct))
                .IsEqualTo(TheoryBankFailure.NotFoundOrForbidden);

            await manage.GrantAsync(owner.Id, id, editor.Id, TheoryQuestionPermission.Edit, ct);
            var updated = await manage.SaveAsync(new(editor.Id, id, content with { Title = "Synthetic two" }, now.AddMinutes(1)), ct);
            await Assert.That(updated.Saved!.Version).IsEqualTo(2);
            await Assert.That(await db.TheoryQuestionRevisions.Where(revision => revision.Id == created.Saved.RevisionId)
                .Select(revision => revision.Title).SingleAsync(ct)).IsEqualTo("Synthetic one");
            var copied = await bank.CopyAsync(editor.Id, id, now.AddMinutes(2), ct);
            await Assert.That(copied.Saved!.SourceId).IsEqualTo(created.Saved.SourceId);
            await Assert.That(copied.Saved.QuestionId == id).IsFalse();
            var originalOptions = await db.Set<TheoryQuestionOption>().Where(option => option.RevisionId == updated.Saved.RevisionId)
                .Select(option => option.Id).ToArrayAsync(ct);
            var copiedOptions = await db.Set<TheoryQuestionOption>().Where(option => option.RevisionId == copied.Saved.RevisionId)
                .Select(option => option.Id).ToArrayAsync(ct);
            await Assert.That(originalOptions.Intersect(copiedOptions).Any()).IsFalse();

            await manage.SetStateAsync(owner.Id, id, TheoryQuestionState.Retired, now.AddMinutes(3), ct);
            await Assert.That((await bank.ReadAsync(owner.Id, id, ct))!.Question.State).IsEqualTo(TheoryQuestionState.Retired);
            await Assert.That((await bank.ReadAsync(editor.Id, copied.Saved.QuestionId, ct))!.Question.State).IsEqualTo(TheoryQuestionState.Draft);
            var forgedFile = await manage.SaveAsync(new(owner.Id, null, content with { FileIds = [Guid.NewGuid()] }, now), ct);
            await Assert.That(forgedFile.Failure).IsEqualTo(TheoryBankFailure.FileNotAuthorized);
            await Assert.That(db.ChangeTracker.Entries<TheoryQuestion>().Any(entry => entry.State == EntityState.Added)).IsFalse();
            await Assert.That(await db.Challenges.CountAsync(ct)).IsEqualTo(0);
            await manage.GrantAsync(owner.Id, id, consumer.Id, null, ct);
            await Assert.That((await bank.ListAsync(consumer.Id, null, null, 0, 10, ct)).Total).IsEqualTo(0);
        });
    }

    private static User User(string name, UserRole role, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.invalid", NormalizedEmail = $"{name}@example.invalid".ToUpperInvariant(), Role = role,
        PasswordHash = "not-a-login", CreatedAt = now, UpdatedAt = now
    };
}
