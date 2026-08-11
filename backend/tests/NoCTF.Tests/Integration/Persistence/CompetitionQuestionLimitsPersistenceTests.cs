using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Questions;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionQuestions")]
public sealed class CompetitionQuestionLimitsPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public Task Creation_validates_references_and_handler_scope(CancellationToken ct) =>
        RunAsync(async fixture =>
        {
            await using var db = fixture.CreateDbContext();
            var store = CreateStore(db);

            var platform = await store.CreateAsync(Command(
                fixture,
                fixture.MemberOneId,
                CompetitionQuestionSubject.Platform,
                null,
                "赛事流程咨询",
                "请问本场比赛的暂停规则是什么？",
                0), ct);
            await Assert.That(platform.Failure).IsNull();

            var platformWithChallenge = await store.CreateAsync(Command(
                fixture,
                fixture.MemberOneId,
                CompetitionQuestionSubject.Platform,
                fixture.PublishedBindingId,
                "错误的平台引用",
                "平台咨询不应携带比赛题目引用。",
                1), ct);
            await Assert.That(platformWithChallenge.Failure)
                .IsEqualTo(CompetitionQuestionFailure.InvalidChallengeReference);

            foreach (var challengeId in new Guid?[]
                     {
                         null,
                         Guid.NewGuid(),
                         fixture.UnpublishedBindingId,
                         fixture.OtherCompetitionBindingId
                     })
            {
                var rejected = await store.CreateAsync(Command(
                    fixture,
                    fixture.MemberOneId,
                    CompetitionQuestionSubject.Challenge,
                    challengeId,
                    $"无效题目引用 {challengeId}",
                    "关联题目必须已发布且属于当前比赛。",
                    2), ct);
                await Assert.That(rejected.Failure)
                    .IsEqualTo(CompetitionQuestionFailure.InvalidChallengeReference);
            }

            var challenge = await store.CreateAsync(Command(
                fixture,
                fixture.MemberOneId,
                CompetitionQuestionSubject.Challenge,
                fixture.PublishedBindingId,
                "题目环境咨询",
                "题目环境启动后无法连接公开端口。",
                3), ct);
            await Assert.That(challenge.Failure).IsNull();
            var questionId = challenge.Question!.Id;

            await Assert.That((await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.JudgeId,
                ct))!.Access).IsEqualTo(CompetitionQuestionAccess.Handler);
            await Assert.That((await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.ObserverId,
                ct))!.Access).IsEqualTo(CompetitionQuestionAccess.Observer);
            await Assert.That((await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.ChallengeOwnerId,
                ct))!.Access).IsEqualTo(CompetitionQuestionAccess.Handler);
            await Assert.That(await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.OtherChallengeOwnerId,
                ct)).IsNull();
            await Assert.That(await store.FindAsync(
                fixture.CompetitionId,
                platform.Question!.Id,
                fixture.ChallengeOwnerId,
                ct)).IsNull();
            var authorList = await store.ListAsync(new(
                fixture.CompetitionId,
                fixture.ChallengeOwnerId,
                null,
                null,
                null,
                20), ct);
            await Assert.That(authorList.Items.Select(item => item.Id))
                .IsEquivalentTo([questionId]);
            await Assert.That((await store.ListAsync(new(
                fixture.CompetitionId,
                fixture.OtherChallengeOwnerId,
                null,
                null,
                null,
                20), ct)).Items).IsEmpty();
            await Assert.That((await store.ListAsync(new(
                fixture.CompetitionId,
                fixture.ObserverId,
                null,
                null,
                CompetitionQuestionStatus.Pending,
                20), ct)).Items.Count).IsEqualTo(2);

            var competition = await db.Competitions.SingleAsync(
                candidate => candidate.Id == fixture.CompetitionId,
                ct);
            competition.AllowChallengeOwnersToHandleQuestions = false;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await Assert.That(await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.ChallengeOwnerId,
                ct)).IsNull();

            var observerReply = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                questionId,
                fixture.ObserverId,
                "观察员只能读取，不能回复咨询。",
                0,
                fixture.Now.AddMinutes(10)), ct);
            await Assert.That(observerReply.Failure)
                .IsEqualTo(CompetitionQuestionFailure.Forbidden);

            var judgeReply = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                questionId,
                fixture.JudgeId,
                "裁判已确认题目服务正在恢复。",
                0,
                fixture.Now.AddMinutes(11)), ct);
            await Assert.That(judgeReply.Failure).IsNull();
            await Assert.That(judgeReply.Question!.Entries.Last().ActorRole)
                .IsEqualTo(CompetitionQuestionParticipantRole.Judge);

            var teammateView = await store.FindAsync(
                fixture.CompetitionId,
                questionId,
                fixture.MemberTwoId,
                ct);
            await Assert.That(teammateView!.Access).IsEqualTo(CompetitionQuestionAccess.Asker);
        }, ct);

    [Test]
    [Timeout(300_000)]
    public Task Active_and_message_limits_follow_ticket_state(CancellationToken ct) =>
        RunAsync(async fixture =>
        {
            await using var db = fixture.CreateDbContext();
            var store = CreateStore(db);
            var questions = new List<CompetitionQuestionView>();
            for (var index = 0; index < 5; index++)
            {
                var created = await store.CreateAsync(Command(
                    fixture,
                    fixture.MemberOneId,
                    CompetitionQuestionSubject.Platform,
                    null,
                    $"平台咨询 {index + 1}",
                    $"这是第 {index + 1} 个仍在处理中的平台咨询。",
                    index), ct);
                await Assert.That(created.Failure).IsNull();
                questions.Add(created.Question!);
                db.ChangeTracker.Clear();
            }

            var sixth = await store.CreateAsync(Command(
                fixture,
                fixture.MemberTwoId,
                CompetitionQuestionSubject.Platform,
                null,
                "第六个咨询",
                "该咨询必须被队伍共享的活跃上限拒绝。",
                6), ct);
            await Assert.That(sixth.Failure)
                .IsEqualTo(CompetitionQuestionFailure.TeamActiveQuestionLimitReached);
            await Assert.That(sixth.Limit).IsEqualTo(5);

            var resolved = await store.ChangeStatusAsync(new(
                fixture.CompetitionId,
                questions[0].Id,
                fixture.ManagerId,
                CompetitionQuestionStatus.Resolved,
                questions[0].Revision,
                fixture.Now.AddMinutes(20)), ct);
            await Assert.That(resolved.Failure).IsNull();
            db.ChangeTracker.Clear();
            var closed = await store.ChangeStatusAsync(new(
                fixture.CompetitionId,
                questions[1].Id,
                fixture.ManagerId,
                CompetitionQuestionStatus.Closed,
                questions[1].Revision,
                fixture.Now.AddMinutes(21)), ct);
            await Assert.That(closed.Failure).IsNull();
            db.ChangeTracker.Clear();

            for (var index = 0; index < 2; index++)
            {
                var replacement = await store.CreateAsync(Command(
                    fixture,
                    fixture.MemberOneId,
                    CompetitionQuestionSubject.Platform,
                    null,
                    $"替代咨询 {index + 1}",
                    "已解决和已关闭咨询不计入活跃数量。",
                    30 + index), ct);
                await Assert.That(replacement.Failure).IsNull();
                db.ChangeTracker.Clear();
            }

            var blockedReopen = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                resolved.Question!.Id,
                fixture.MemberTwoId,
                "重新追问会再次占用一个活跃咨询名额。",
                resolved.Question.Revision,
                fixture.Now.AddMinutes(40)), ct);
            await Assert.That(blockedReopen.Failure)
                .IsEqualTo(CompetitionQuestionFailure.TeamActiveQuestionLimitReached);

            var released = await store.ChangeStatusAsync(new(
                fixture.CompetitionId,
                questions[2].Id,
                fixture.ManagerId,
                CompetitionQuestionStatus.Closed,
                questions[2].Revision,
                fixture.Now.AddMinutes(41)), ct);
            await Assert.That(released.Failure).IsNull();
            db.ChangeTracker.Clear();
            var reopened = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                resolved.Question.Id,
                fixture.MemberTwoId,
                "现在已有空余活跃名额，允许继续追问。",
                resolved.Question.Revision,
                fixture.Now.AddMinutes(42)), ct);
            await Assert.That(reopened.Failure).IsNull();
            db.ChangeTracker.Clear();

            var thirdParticipantMessage = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                reopened.Question!.Id,
                fixture.MemberOneId,
                "这是工作人员回复前的第三条队伍消息。",
                reopened.Question.Revision,
                fixture.Now.AddMinutes(43)), ct);
            await Assert.That(thirdParticipantMessage.Failure).IsNull();
            db.ChangeTracker.Clear();
            var fourthParticipantMessage = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                thirdParticipantMessage.Question!.Id,
                fixture.MemberTwoId,
                "第四条消息必须等待工作人员回复。",
                thirdParticipantMessage.Question.Revision,
                fixture.Now.AddMinutes(44)), ct);
            await Assert.That(fourthParticipantMessage.Failure)
                .IsEqualTo(CompetitionQuestionFailure.ParticipantMessageLimitReached);
            await Assert.That(fourthParticipantMessage.Limit).IsEqualTo(3);

            var handlerReply = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                thirdParticipantMessage.Question.Id,
                fixture.ManagerId,
                "工作人员回复后，队伍连续发送额度已经重置。",
                thirdParticipantMessage.Question.Revision,
                fixture.Now.AddMinutes(45)), ct);
            await Assert.That(handlerReply.Failure).IsNull();
            db.ChangeTracker.Clear();
            var afterReset = await store.AddMessageAsync(new(
                fixture.CompetitionId,
                handlerReply.Question!.Id,
                fixture.MemberTwoId,
                "额度重置后允许继续补充消息。",
                handlerReply.Question.Revision,
                fixture.Now.AddMinutes(46)), ct);
            await Assert.That(afterReset.Failure).IsNull();
            await Assert.That(afterReset.Question!.ParticipantMessagesRemaining).IsEqualTo(2);
        }, ct);

    [Test]
    [Timeout(300_000)]
    public Task Team_lock_prevents_concurrent_members_from_exceeding_limits(
        CancellationToken ct) => RunAsync(async fixture =>
    {
        await using (var setup = fixture.CreateDbContext())
        {
            var competition = await setup.Competitions.SingleAsync(
                item => item.Id == fixture.CompetitionId,
                ct);
            competition.MaxActiveQuestionsPerTeam = 1;
            await setup.SaveChangesAsync(ct);
        }

        async Task<CompetitionQuestionMutationResult> CreateAsync(
            Guid actorId,
            string suffix)
        {
            await using var db = fixture.CreateDbContext();
            return await CreateStore(db).CreateAsync(Command(
                fixture,
                actorId,
                CompetitionQuestionSubject.Platform,
                null,
                $"并发咨询 {suffix}",
                $"来自不同队员的并发创建请求 {suffix}。",
                60), ct);
        }

        var results = await Task.WhenAll(
            CreateAsync(fixture.MemberOneId, "A"),
            CreateAsync(fixture.MemberTwoId, "B"));
        await Assert.That(results.Count(result => result.Failure is null)).IsEqualTo(1);
        await Assert.That(results.Count(result =>
                result.Failure == CompetitionQuestionFailure.TeamActiveQuestionLimitReached))
            .IsEqualTo(1);
    }, ct);

    [Test]
    [Timeout(300_000)]
    public Task List_projects_only_the_requested_page_from_long_history(
        CancellationToken ct) => RunAsync(async fixture =>
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        await using (var setup = fixture.CreateDbContext())
        {
            for (var index = 0; index < 250; index++)
            {
                var sentAt = fixture.Now.AddDays(-30).AddMinutes(index);
                var root = new Notification
                {
                    Id = Guid.CreateVersion7(sentAt),
                    SourceType = NotificationSourceType.User,
                    SourceId = fixture.MemberOneId,
                    TargetType = NotificationTargetType.CompetitionCollaborators,
                    TargetId = fixture.CompetitionId,
                    Kind = NotificationKind.QuestionOpened,
                    ContentJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        subject = CompetitionQuestionSubject.Platform,
                        title = $"历史咨询 {index}",
                        body = "已经关闭的历史咨询不应逐条加载完整线程。",
                        teamId = fixture.TeamId,
                        competitionChallengeId = (Guid?)null,
                        gameplayFactId = (Guid?)null,
                        status = CompetitionQuestionStatus.Pending
                    }, jsonOptions),
                    SentAt = sentAt
                };
                setup.Notifications.AddRange(root, new Notification
                {
                    Id = Guid.CreateVersion7(sentAt.AddSeconds(1)),
                    SourceType = NotificationSourceType.User,
                    SourceId = fixture.ManagerId,
                    TargetType = root.TargetType,
                    TargetId = root.TargetId,
                    Kind = NotificationKind.QuestionStatusChanged,
                    ContentJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        from = CompetitionQuestionStatus.Pending,
                        to = CompetitionQuestionStatus.Closed,
                        actorRole = CompetitionQuestionParticipantRole.CompetitionManager
                    }, jsonOptions),
                    SentAt = sentAt.AddSeconds(1),
                    ReplyToId = root.Id
                });
            }
            await setup.SaveChangesAsync(ct);
        }

        var counter = new QueryCounter();
        await using var db = fixture.CreateDbContext(counter);
        var store = CreateStore(db);
        var listed = await store.ListAsync(new(
            fixture.CompetitionId,
            fixture.ManagerId,
            null,
            CompetitionQuestionSubject.Platform,
            CompetitionQuestionStatus.Closed,
            20), ct);

        await Assert.That(listed.Items).Count().IsEqualTo(20);
        await Assert.That(listed.Items.All(question =>
            question.Status == CompetitionQuestionStatus.Closed)).IsTrue();
        await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(10);

        counter.Reset();
        var created = await store.CreateAsync(Command(
            fixture,
            fixture.MemberOneId,
            CompetitionQuestionSubject.Platform,
            null,
            "长历史后的新咨询",
            "活跃数量检查必须使用集合查询，而不是逐条读取历史线程。",
            1000), ct);
        await Assert.That(created.Failure).IsNull();
        await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(14);
    }, ct);

    [Test]
    [Timeout(300_000)]
    public Task List_keyset_pages_long_history_without_gaps_and_preserves_visibility_filters(
        CancellationToken ct) => RunAsync(async fixture =>
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        await using (var setup = fixture.CreateDbContext())
        {
            for (var index = 0; index < 180; index++)
            {
                var sentAt = fixture.Now.AddDays(-60).AddSeconds(index / 2 * 2);
                var subject = index % 2 == 0
                    ? CompetitionQuestionSubject.Challenge
                    : CompetitionQuestionSubject.Platform;
                var status = (index % 4) switch
                {
                    1 => CompetitionQuestionStatus.Closed,
                    2 => CompetitionQuestionStatus.Resolved,
                    3 => CompetitionQuestionStatus.Replied,
                    _ => CompetitionQuestionStatus.Pending
                };
                var root = new Notification
                {
                    Id = Guid.CreateVersion7(sentAt),
                    SourceType = NotificationSourceType.User,
                    SourceId = index < 160 ? fixture.MemberOneId : fixture.OtherMemberId,
                    TargetType = NotificationTargetType.CompetitionCollaborators,
                    TargetId = fixture.CompetitionId,
                    Kind = NotificationKind.QuestionOpened,
                    ContentJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        subject,
                        title = $"分页咨询 {index:D3}",
                        body = "该咨询用于验证有界 keyset 分页。",
                        teamId = index < 160 ? fixture.TeamId : fixture.OtherTeamId,
                        competitionChallengeId = subject == CompetitionQuestionSubject.Challenge
                            ? fixture.PublishedBindingId
                            : (Guid?)null,
                        gameplayFactId = (Guid?)null,
                        status = CompetitionQuestionStatus.Pending
                    }, jsonOptions),
                    SentAt = sentAt
                };
                setup.Notifications.Add(root);
                if (status != CompetitionQuestionStatus.Pending)
                {
                    setup.Notifications.Add(new Notification
                    {
                        Id = Guid.CreateVersion7(sentAt.AddSeconds(1)),
                        SourceType = NotificationSourceType.User,
                        SourceId = fixture.ManagerId,
                        TargetType = root.TargetType,
                        TargetId = root.TargetId,
                        Kind = NotificationKind.QuestionStatusChanged,
                        ContentJson = JsonSerializer.Serialize(new
                        {
                            schemaVersion = 1,
                            from = CompetitionQuestionStatus.Pending,
                            to = status,
                            actorRole = CompetitionQuestionParticipantRole.CompetitionManager
                        }, jsonOptions),
                        SentAt = sentAt.AddSeconds(1),
                        ReplyToId = root.Id
                    });
                }
            }
            await setup.SaveChangesAsync(ct);
        }

        async Task<CompetitionQuestionView[]> ReadAllAsync(
            Guid actorUserId,
            Guid? competitionChallengeId = null,
            CompetitionQuestionSubject? subject = null,
            CompetitionQuestionStatus? status = null,
            int limit = 37)
        {
            var items = new List<CompetitionQuestionView>();
            CompetitionQuestionPagePosition? position = null;
            await using var db = fixture.CreateDbContext();
            var store = CreateStore(db);
            do
            {
                var page = await store.ListAsync(new(
                    fixture.CompetitionId,
                    actorUserId,
                    competitionChallengeId,
                    subject,
                    status,
                    limit,
                    position), ct);
                await Assert.That(page.Items.Count).IsLessThanOrEqualTo(limit);
                if (page.NextPosition is null)
                    await Assert.That(page.Items.Count).IsLessThan(limit);
                else
                    await Assert.That(page.Items.Count).IsEqualTo(limit);
                items.AddRange(page.Items);
                position = page.NextPosition;
            } while (position is not null);
            return items.ToArray();
        }

        var managerItems = await ReadAllAsync(fixture.ManagerId);
        await Assert.That(managerItems.Length).IsEqualTo(180);
        await Assert.That(managerItems.Select(item => item.Id).Distinct().Count())
            .IsEqualTo(180);
        await Assert.That(managerItems.Zip(managerItems.Skip(1)).All(pair =>
            pair.First.UpdatedAt > pair.Second.UpdatedAt
            || pair.First.UpdatedAt == pair.Second.UpdatedAt
            && pair.First.Id.CompareTo(pair.Second.Id) > 0)).IsTrue();

        var participantItems = await ReadAllAsync(fixture.MemberOneId);
        await Assert.That(participantItems.Length).IsEqualTo(160);
        await Assert.That(participantItems.All(item => item.TeamId == fixture.TeamId)).IsTrue();
        var otherParticipantItems = await ReadAllAsync(fixture.OtherMemberId);
        await Assert.That(otherParticipantItems.Length).IsEqualTo(20);
        await Assert.That(otherParticipantItems.All(item => item.TeamId == fixture.OtherTeamId)).IsTrue();
        var challengeOwnerItems = await ReadAllAsync(fixture.ChallengeOwnerId);
        await Assert.That(challengeOwnerItems.Length).IsEqualTo(90);
        await Assert.That(challengeOwnerItems.All(item =>
            item.CompetitionChallengeId == fixture.PublishedBindingId)).IsTrue();
        var observerItems = await ReadAllAsync(fixture.ObserverId);
        await Assert.That(observerItems.Length).IsEqualTo(180);

        var closedItems = await ReadAllAsync(
            fixture.ManagerId,
            status: CompetitionQuestionStatus.Closed);
        await Assert.That(closedItems.Length).IsEqualTo(45);
        await Assert.That(closedItems.All(item =>
            item.Status == CompetitionQuestionStatus.Closed)).IsTrue();
        await using (var exactPageDb = fixture.CreateDbContext())
        {
            var exactLastPage = await CreateStore(exactPageDb).ListAsync(new(
                fixture.CompetitionId,
                fixture.ManagerId,
                null,
                null,
                CompetitionQuestionStatus.Closed,
                45), ct);
            await Assert.That(exactLastPage.Items.Count).IsEqualTo(45);
            await Assert.That(exactLastPage.NextPosition).IsNull();
        }
        var challengeItems = await ReadAllAsync(
            fixture.ManagerId,
            competitionChallengeId: fixture.PublishedBindingId,
            subject: CompetitionQuestionSubject.Challenge);
        await Assert.That(challengeItems.Length).IsEqualTo(90);
        await Assert.That(challengeItems.All(item =>
            item.Subject == CompetitionQuestionSubject.Challenge
            && item.CompetitionChallengeId == fixture.PublishedBindingId)).IsTrue();
    }, ct);

    private static CreateCompetitionQuestionCommand Command(
        Fixture fixture,
        Guid actorId,
        CompetitionQuestionSubject subject,
        Guid? challengeId,
        string title,
        string body,
        int minuteOffset) =>
        new(
            fixture.CompetitionId,
            subject,
            challengeId,
            null,
            actorId,
            title,
            body,
            fixture.Now.AddMinutes(minuteOffset));

    private static CompetitionQuestionStore CreateStore(NoCtfDbContext db)
    {
        var outbox = new RecordingOutbox();
        return new(
            db,
            outbox,
            new CompetitionEventStore(db, outbox),
            NullLogger<CompetitionQuestionStore>.Instance);
    }

    private static async Task RunAsync(
        Func<Fixture, Task> test,
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_question_limits")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var fixture = new Fixture(postgres.GetConnectionString());
            await fixture.InitializeAsync(ct);
            await test(fixture);
        });
    }

    private sealed class Fixture(string connectionString)
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        public Guid CompetitionId { get; } = Guid.CreateVersion7();
        public Guid OtherCompetitionId { get; } = Guid.CreateVersion7();
        public Guid PublishedBindingId { get; } = Guid.CreateVersion7();
        public Guid UnpublishedBindingId { get; } = Guid.CreateVersion7();
        public Guid OtherCompetitionBindingId { get; } = Guid.CreateVersion7();
        public Guid OwnerId { get; } = Guid.CreateVersion7();
        public Guid ManagerId { get; } = Guid.CreateVersion7();
        public Guid JudgeId { get; } = Guid.CreateVersion7();
        public Guid ObserverId { get; } = Guid.CreateVersion7();
        public Guid ChallengeOwnerId { get; } = Guid.CreateVersion7();
        public Guid OtherChallengeOwnerId { get; } = Guid.CreateVersion7();
        public Guid MemberOneId { get; } = Guid.CreateVersion7();
        public Guid MemberTwoId { get; } = Guid.CreateVersion7();
        public Guid OtherMemberId { get; } = Guid.CreateVersion7();
        public Guid TeamId { get; } = Guid.CreateVersion7();
        public Guid OtherTeamId { get; } = Guid.CreateVersion7();

        public NoCtfDbContext CreateDbContext(params IInterceptor[] interceptors) => new(
            new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(interceptors)
                .Options);

        public async Task InitializeAsync(CancellationToken ct)
        {
            await using var db = CreateDbContext();
            await db.Database.MigrateAsync(ct);
            db.Users.AddRange(
                User(OwnerId, "owner", UserRole.Organizer),
                User(ManagerId, "manager", UserRole.Organizer),
                User(JudgeId, "judge", UserRole.Organizer),
                User(ObserverId, "observer", UserRole.Organizer),
                User(ChallengeOwnerId, "challenge-owner", UserRole.Organizer),
                User(OtherChallengeOwnerId, "other-owner", UserRole.Organizer),
                User(MemberOneId, "member-one", UserRole.User),
                User(MemberTwoId, "member-two", UserRole.User),
                User(OtherMemberId, "other-member", UserRole.User));
            db.Competitions.AddRange(
                Competition(CompetitionId, OwnerId, "Question competition"),
                Competition(OtherCompetitionId, OtherChallengeOwnerId, "Other competition"));
            var publishedTemplateId = Guid.CreateVersion7();
            var unpublishedTemplateId = Guid.CreateVersion7();
            var otherTemplateId = Guid.CreateVersion7();
            db.Challenges.AddRange(
                Challenge(publishedTemplateId, ChallengeOwnerId, "Published challenge"),
                Challenge(unpublishedTemplateId, ChallengeOwnerId, "Unpublished challenge"),
                Challenge(otherTemplateId, OtherChallengeOwnerId, "Other challenge"));
            db.CompetitionChallenges.AddRange(
                Binding(PublishedBindingId, CompetitionId, publishedTemplateId, true, 1),
                Binding(UnpublishedBindingId, CompetitionId, unpublishedTemplateId, false, 2),
                Binding(OtherCompetitionBindingId, OtherCompetitionId, otherTemplateId, true, 1));
            db.Teams.AddRange(
                Team(TeamId, CompetitionId, "Question team", MemberOneId, [MemberOneId, MemberTwoId]),
                Team(OtherTeamId, CompetitionId, "Other team", OtherMemberId, [OtherMemberId]));
            await db.SaveChangesAsync(ct);
        }

        private User User(Guid id, string name, UserRole role) => new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        private Competition Competition(Guid id, Guid ownerId, string title) => new()
        {
            Id = id,
            OwnerId = ownerId,
            ManagerIds = id == CompetitionId ? [ManagerId] : [],
            JudgeIds = id == CompetitionId ? [JudgeId] : [],
            ObserverIds = id == CompetitionId ? [ObserverId] : [],
            Title = title,
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = Now,
            FlagDerivationSecret = new byte[32],
            StartAt = Now.AddHours(-1),
            EndAt = Now.AddHours(2),
            Status = CompetitionStatus.Running,
            MaxActiveQuestionsPerTeam = 5,
            MaxParticipantMessagesBeforeHandlerReply = 3,
            AllowChallengeOwnersToHandleQuestions = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        private Challenge Challenge(Guid id, Guid ownerId, string title) => new()
        {
            Id = id,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = title,
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":1}""",
            CreatedAt = Now,
            UpdatedAt = Now
        };

        private CompetitionChallenge Binding(
            Guid id,
            Guid competitionId,
            Guid challengeId,
            bool published,
            int order) => new()
            {
                Id = id,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                BaseScore = 500,
                Order = order,
                IsPublished = published,
                RulesJson = """{"schemaVersion":1}""",
                UpdatedAt = Now
            };

        private static Team Team(
            Guid id,
            Guid competitionId,
            string name,
            Guid captainId,
            Guid[] members) => new()
            {
                Id = id,
                CompetitionId = competitionId,
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                CaptainId = captainId,
                MemberIds = members,
                InvitationToken = Guid.NewGuid().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = DateTimeOffset.UtcNow
            };
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public void Reset() => ReaderCount = 0;

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            ReaderCount++;
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }
    }
}
