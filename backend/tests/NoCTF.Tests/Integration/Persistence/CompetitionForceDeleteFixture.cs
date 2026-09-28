using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

internal sealed class CompetitionForceDeleteFixture
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public Guid OwnerId { get; } = Guid.NewGuid();
    public Guid TemplateId { get; } = Guid.NewGuid();
    public Guid FileId { get; } = Guid.NewGuid();
    public Guid WriteUpFileId { get; } = Guid.NewGuid();
    public Guid SharedFileId { get; } = Guid.NewGuid();
    public Guid[] AdditionalPatchFileIds { get; } = [Guid.NewGuid(), Guid.NewGuid()];
    public Guid[] CleanupFileIds => [FileId, WriteUpFileId, SharedFileId, .. AdditionalPatchFileIds];
    public Guid RootId { get; } = Guid.NewGuid();
    public Guid MemberId { get; } = Guid.NewGuid();
    public Guid[] RuntimeIds { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    public ForceDeleteCompetitionCommand Command => new(Id, OwnerId, "Delete fixture", "Delete this disposable test competition.", Now);

    public async Task SeedAsync(NoCtfDbContext db, CancellationToken ct)
    {
        db.Users.Add(new User
        {
            Id = OwnerId, UserName = $"owner-{OwnerId:N}", NormalizedUserName = $"OWNER-{OwnerId:N}",
            Email = $"{OwnerId:N}@example.test", PasswordHash = "unused", Kind = UserKind.Human,
            Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active, CreatedAt = Now, UpdatedAt = Now
        });
        db.Files.AddRange(File(FileId), File(WriteUpFileId), File(SharedFileId));
        db.Files.AddRange(AdditionalPatchFileIds.Select(File));
        db.Competitions.AddRange(Competition(Id, "Delete fixture", FileId), Competition(OtherId, "Keep fixture", SharedFileId));
        db.Challenges.Add(new AwdpChallenge
        {
            Id = TemplateId, OwnerId = OwnerId, Visibility = ChallengeVisibility.Private,
            Title = "Global template", Direction = "Pwn", Definition = TestConfigurations.Definition(GameMode.Awdp), CreatedAt = Now, UpdatedAt = Now
        });
        db.Set<ChallengeAttachment>().Add(new ChallengeAttachment
        {
            Id = Guid.NewGuid(), ChallengeId = TemplateId, FileId = SharedFileId, CreatedAt = Now
        });
        db.ChallengeFlags.Add(new TemplateChallengeFlag
        {
            Id = Guid.NewGuid(), ChallengeId = TemplateId, Flag = "flag{keep}", FlagSha256 = new byte[32], CreatedAt = Now
        });
        var challenge = Guid.NewGuid();
        var team = Guid.NewGuid();
        db.CompetitionChallenges.Add(new AwdpCompetitionChallenge
        {
            Id = challenge, CompetitionId = Id, ChallengeId = TemplateId, Rules = TestConfigurations.Rules(GameMode.Awdp), UpdatedAt = Now
        });
        db.CompetitionChallenges.Add(new AwdpCompetitionChallenge
        {
            Id = Guid.NewGuid(), CompetitionId = OtherId, ChallengeId = TemplateId, Rules = TestConfigurations.Rules(GameMode.Awdp), UpdatedAt = Now
        });
        db.Teams.Add(new Team
        {
            Id = team, CompetitionId = Id, Name = "Team", CaptainId = OwnerId, MemberIds = [OwnerId],
            InvitationToken = Guid.NewGuid().ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = Now, WriteUpFileId = WriteUpFileId,
            WriteUpSubmittedByUserId = OwnerId, WriteUpSubmittedAt = Now
        });
        foreach (var (runtimeId, index) in RuntimeIds.Select((id, index) => (id, index)))
        {
            var factId = Guid.NewGuid();
            var patchId = Guid.NewGuid();
            db.GameplayFacts.Add(new FixAttemptGameplayFact
            {
                Id = factId, CompetitionId = Id, CompetitionChallengeId = challenge, TeamId = team,
                ActorUserId = OwnerId, State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong, ReferenceKind = GameplayFactReferenceKind.PatchUpload,
                ReferenceId = patchId, OccurredAt = Now, UpdatedAt = Now
            });
            db.RuntimeInstances.Add(new AwdpTargetRuntimeInstance
            {
                Id = runtimeId, CompetitionId = Id, CompetitionChallengeId = challenge, TeamId = team,
                GameplayFactId = factId, RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Stopped, CreatedAt = Now, StoppedAt = Now
            });
            db.PatchUploads.Add(new PatchUpload
            {
                Id = patchId, CompetitionId = Id, CompetitionChallengeId = challenge, TeamId = team,
                RuntimeInstanceId = runtimeId, UploadedByUserId = OwnerId,
                FileId = index == 0 ? SharedFileId : AdditionalPatchFileIds[index - 1], UploadedAt = Now
            });
        }
        db.Notifications.Add(Notification(RootId, Id));
        var member = Notification(MemberId, null);
        member.ThreadRootId = RootId; // No ReplyToId or direct competition reference.
        db.Notifications.Add(member);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public Notification Notification(
        Guid id,
        Guid? competitionId,
        NotificationKind kind = NotificationKind.Message)
    {
        var notification = NotificationGeneratedCatalog.Create(kind);
        notification.Id = id;
        notification.SourceType = NotificationSourceType.User;
        notification.SourceId = OwnerId;
        notification.TargetType = NotificationTargetType.User;
        notification.TargetId = OwnerId;
        notification.RelatedType = competitionId.HasValue ? EntityReferenceKind.Competition : null;
        notification.RelatedId = competitionId;
        notification.SentAt = Now;
        return notification;
    }

    private Competition Competition(Guid id, string title, Guid poster) => new AwdpCompetition
    {
        Id = id, OwnerId = OwnerId, Title = title, PosterFileId = poster,
        ModeConfiguration = TestConfigurations.Competition(GameMode.Awdp), FlagDerivationSecret = new byte[32],
        StartAt = Now.AddHours(-2), EndAt = Now.AddHours(-1), Status = CompetitionStatus.Finished,
        CreatedAt = Now, UpdatedAt = Now
    };

    private StoredFile File(Guid id) => new()
    {
        Id = id, ObjectKey = $"deletion-tests/{id:N}", FileName = "fixture.tar.gz",
        ContentType = "application/gzip", ByteLength = 1, Sha256 = new byte[32], CreatedAt = Now
    };
}
