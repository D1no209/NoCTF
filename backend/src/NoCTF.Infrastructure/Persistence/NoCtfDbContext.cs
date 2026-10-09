using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Competitions.StaffWebhooks;
using NoCTF.Domain.Teams;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Commands;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Admission;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for all NoCTF business facts and current state.</summary>
public sealed class NoCtfDbContext(
    DbContextOptions<NoCtfDbContext> options,
    TimeProvider? clock = null,
    INotificationChangePublisher? notificationPublisher = null,
    ILogger<NoCtfDbContext>? logger = null)
    : DbContext(options), IDataProtectionKeyContext
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private NotificationChangeTracker? notificationChangeTracker;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (notificationPublisher is null) return;
        notificationChangeTracker ??= new(this, notificationPublisher, logger);
        notificationChangeTracker.Attach();
        optionsBuilder.AddInterceptors(
            notificationChangeTracker.Saves,
            notificationChangeTracker.Transactions);
    }
    public DbSet<LiveSoloMatch> LiveSoloMatches => Set<LiveSoloMatch>();
    public DbSet<LiveSoloAdjudication> LiveSoloAdjudications => Set<LiveSoloAdjudication>();
    public DbSet<LiveSoloRound> LiveSoloRounds => Set<LiveSoloRound>();
    public DbSet<LiveSoloRoundQuestion> LiveSoloRoundQuestions => Set<LiveSoloRoundQuestion>();
    public DbSet<LiveSoloActiveTeamSlot> LiveSoloActiveTeamSlots => Set<LiveSoloActiveTeamSlot>();
    public DbSet<LiveSoloSubmission> LiveSoloSubmissions => Set<LiveSoloSubmission>();
    public DbSet<LiveSoloDownloadEvidence> LiveSoloDownloadEvidences => Set<LiveSoloDownloadEvidence>();
    public DbSet<LiveSoloAttachmentAssignment> LiveSoloAttachmentAssignments => Set<LiveSoloAttachmentAssignment>();
    public DbSet<LiveSoloQuestionGroup> LiveSoloQuestionGroups => Set<LiveSoloQuestionGroup>();
    public DbSet<LiveSoloChallengeSource> LiveSoloChallengeSources => Set<LiveSoloChallengeSource>();
    public DbSet<LiveSoloQuestionExposure> LiveSoloQuestionExposures => Set<LiveSoloQuestionExposure>();
    public DbSet<LiveSoloMediaSession> LiveSoloMediaSessions => Set<LiveSoloMediaSession>();
    public DbSet<LiveSoloMediaGrant> LiveSoloMediaGrants => Set<LiveSoloMediaGrant>();
    public DbSet<LiveSoloMediaParticipant> LiveSoloMediaParticipants => Set<LiveSoloMediaParticipant>();
    public DbSet<LiveSoloProgramSegment> LiveSoloProgramSegments => Set<LiveSoloProgramSegment>();
    public DbSet<LiveSoloProgramCapture> LiveSoloProgramCaptures => Set<LiveSoloProgramCapture>();
    public DbSet<LiveSoloProgramFrame> LiveSoloProgramFrames => Set<LiveSoloProgramFrame>();
    public DbSet<LiveSoloRecording> LiveSoloRecordings => Set<LiveSoloRecording>();
    public DbSet<LiveSoloRecordingDecision> LiveSoloRecordingDecisions => Set<LiveSoloRecordingDecision>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionProgression> CompetitionProgressions => Set<CompetitionProgression>();
    public DbSet<CompetitionWebhookOutboxEvent> CompetitionWebhookOutboxEvents => Set<CompetitionWebhookOutboxEvent>();
    public DbSet<CompetitionWebhookDeliveryRecord> CompetitionWebhookDeliveries => Set<CompetitionWebhookDeliveryRecord>();
    public DbSet<CompetitionWebhookFrozenProjection> CompetitionWebhookFrozenProjections => Set<CompetitionWebhookFrozenProjection>();
    public DbSet<StaffWebhookStream> StaffWebhookStreams => Set<StaffWebhookStream>();
    public DbSet<StaffWebhookTarget> StaffWebhookTargets => Set<StaffWebhookTarget>();
    public DbSet<StaffWebhookWorkItem> StaffWebhookWorkItems => Set<StaffWebhookWorkItem>();
    public DbSet<StaffWebhookEvent> StaffWebhookEvents => Set<StaffWebhookEvent>();
    public DbSet<StaffWebhookDelivery> StaffWebhookDeliveries => Set<StaffWebhookDelivery>();
    public DbSet<ProgressionNode> ProgressionNodes => Set<ProgressionNode>();
    public DbSet<ProgressionEdge> ProgressionEdges => Set<ProgressionEdge>();
    public DbSet<CompetitionBadge> CompetitionBadges => Set<CompetitionBadge>();
    public DbSet<TeamProgressionNodeState> TeamProgressionNodeStates => Set<TeamProgressionNodeState>();
    public DbSet<TeamProgressionNodeVisit> TeamProgressionNodeVisits => Set<TeamProgressionNodeVisit>();
    public DbSet<TeamProgressionBadgeState> TeamProgressionBadgeStates => Set<TeamProgressionBadgeState>();
    public DbSet<UserBadgeGrant> UserBadgeGrants => Set<UserBadgeGrant>();
    public DbSet<UserBadgeTransition> UserBadgeTransitions => Set<UserBadgeTransition>();
    public DbSet<CompetitionEvent> CompetitionEvents => Set<CompetitionEvent>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<CompetitionChallenge> CompetitionChallenges => Set<CompetitionChallenge>();
    public DbSet<ChallengeFlag> ChallengeFlags => Set<ChallengeFlag>();
    public DbSet<RuntimeInstance> RuntimeInstances => Set<RuntimeInstance>();
    public DbSet<ActiveRuntimeSlot> ActiveRuntimeSlots => Set<ActiveRuntimeSlot>();
    public DbSet<RuntimeReceipt> RuntimeReceipts => Set<RuntimeReceipt>();
    public DbSet<RuntimeCapacityLedger> RuntimeCapacityLedgers => Set<RuntimeCapacityLedger>();
    public DbSet<PatchUpload> PatchUploads => Set<PatchUpload>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<NoCTF.Domain.Identity.Mfa.UserTotpCredential> UserTotpCredentials => Set<NoCTF.Domain.Identity.Mfa.UserTotpCredential>();
    public DbSet<NoCTF.Domain.Identity.Mfa.UserMfaRecoveryCode> UserMfaRecoveryCodes => Set<NoCTF.Domain.Identity.Mfa.UserMfaRecoveryCode>();
    public DbSet<NoCTF.Domain.Identity.Passkeys.UserPasskey> UserPasskeys => Set<NoCTF.Domain.Identity.Passkeys.UserPasskey>();
    public DbSet<NoCTF.Domain.Identity.Passkeys.PasskeyCeremony> PasskeyCeremonies => Set<NoCTF.Domain.Identity.Passkeys.PasskeyCeremony>();
    public DbSet<NoCTF.Domain.Identity.Mfa.MfaChallenge> MfaChallenges => Set<NoCTF.Domain.Identity.Mfa.MfaChallenge>();
    public DbSet<SsoFlowEntity> SsoFlows => Set<SsoFlowEntity>();
    public DbSet<RequestAdmissionWindow> RequestAdmissionWindows => Set<RequestAdmissionWindow>();
    public DbSet<RequestAdmissionLease> RequestAdmissionLeases => Set<RequestAdmissionLease>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<GameplayFact> GameplayFacts => Set<GameplayFact>();
    public DbSet<NoCTF.Domain.Challenges.WriteUps.ChallengeWriteUp> ChallengeWriteUps => Set<NoCTF.Domain.Challenges.WriteUps.ChallengeWriteUp>();
    public DbSet<NoCTF.Domain.Challenges.WriteUps.ChallengeWriteUpVersion> ChallengeWriteUpVersions => Set<NoCTF.Domain.Challenges.WriteUps.ChallengeWriteUpVersion>();
    public DbSet<NoCTF.Domain.Challenges.WriteUps.WriteUpUnlockReceipt> WriteUpUnlockReceipts => Set<NoCTF.Domain.Challenges.WriteUps.WriteUpUnlockReceipt>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoCtfDbContext).Assembly);
        ApplyUtcDateTimeOffsetConversions(modelBuilder);
        ApplyConcurrencyTokens(modelBuilder);
    }

    /// <summary>
    /// Persist instants as UTC ticks so the common relational model does not depend on a provider's
    /// timestamp-with-time-zone representation.
    /// </summary>
    private static void ApplyUtcDateTimeOffsetConversions(ModelBuilder modelBuilder)
    {
        var utcConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var utcNullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
        {
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(utcConverter);
            else if (property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(utcNullableConverter);
        }
    }

    private static void ApplyConcurrencyTokens(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(IConcurrencyTracked).IsAssignableFrom(type.ClrType)))
        {
            entityType.FindProperty(nameof(IConcurrencyTracked.ConcurrencyStamp))!
                .IsConcurrencyToken = true;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (StaffWebhookEventCapture.ChangedCompetitions(this).Length != 0)
            throw new InvalidOperationException("Staff-event source mutations require SaveChangesAsync for transactional projection capture.");
        EnsureAppendOnlyFacts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var changed = StaffWebhookEventCapture.ChangedCompetitions(this);
        await using var transaction = changed.Length != 0 && Database.IsRelational() && Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken) : null;
        if (changed.Length != 0)
            await StaffWebhookEventCapture.CaptureAsync(this, changed, timeProvider.GetUtcNow(), cancellationToken);
        EnsureAppendOnlyFacts();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private void EnsureAppendOnlyFacts()
    {
        SynchronizeTypedConfigurationForeignKeys();
        SynchronizeTeamMemberships();
        SynchronizeNormalizedSearchFields();
        SynchronizeActiveRuntimeSlots();
        SynchronizeChallengeFlagIdentities();
        SynchronizeSsoProviderChildren();
        ValidateExternalIdentities();
        foreach (var entry in ChangeTracker.Entries<IConcurrencyTracked>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ConcurrencyStamp = Guid.NewGuid();
        }
        foreach (var entry in ChangeTracker.Entries<CompetitionWebhookOutboxEvent>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            var persistedAt = timeProvider.GetUtcNow();
            entry.Entity.OutboxPersistedAt = persistedAt;
            entry.Entity.NextDispatchAt = persistedAt;
            entry.Entity.CompetitionRevision = ChangeTracker.Entries<Competition>()
                .FirstOrDefault(competition =>
                    competition.Entity.Id == entry.Entity.CompetitionId)
                ?.Entity.ConcurrencyStamp ?? entry.Entity.CompetitionRevision;
        }

        if (ChangeTracker.Entries<CompetitionEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Competition events are immutable and cannot be updated or deleted.");
        }

        if (ChangeTracker.Entries<Notification>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Notifications are immutable and cannot be updated or deleted.");
        }

        if (ChangeTracker.Entries<UserBadgeTransition>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException(
                "Badge transitions are immutable and cannot be updated or deleted.");

        if (ChangeTracker.Entries<StoredFile>().Any(entry => entry.State == EntityState.Modified))
            throw new InvalidOperationException("Stored file metadata is immutable.");
        if (ChangeTracker.Entries<LiveSoloSubmission>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<LiveSoloDownloadEvidence>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<LiveSoloAttachmentAssignment>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Execution-scoped gameplay associations are immutable.");
        if (ChangeTracker.Entries<LiveSoloAdjudication>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<LiveSoloRecordingDecision>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("LiveSolo staff decisions are immutable.");
        if (ChangeTracker.Entries<LiveSoloProgramFrame>().Any(entry => entry.State == EntityState.Modified)
            || ChangeTracker.Entries<LiveSoloProgramFrameQuestion>().Any(entry => entry.State == EntityState.Modified
                || entry.State == EntityState.Added && !ChangeTracker.Entries<LiveSoloProgramFrame>().Any(frame => frame.Entity.Id == entry.Entity.FrameId && frame.State == EntityState.Added)
                || entry.State == EntityState.Deleted && !ChangeTracker.Entries<LiveSoloProgramFrame>().Any(frame => frame.Entity.Id == entry.Entity.FrameId && frame.State == EntityState.Deleted)))
            throw new InvalidOperationException("LiveSolo program state frames are immutable.");
        if (ChangeTracker.Entries<NoCTF.Domain.Challenges.WriteUps.WriteUpUnlockReceipt>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("WriteUp unlock receipts are immutable.");
        foreach (var entry in ChangeTracker.Entries<NoCTF.Domain.Challenges.WriteUps.ChallengeWriteUpVersion>()
                     .Where(entry => entry.State == EntityState.Modified
                         && entry.OriginalValues.GetValue<NoCTF.Domain.Challenges.WriteUps.WriteUpVersionState>("State")
                             != NoCTF.Domain.Challenges.WriteUps.WriteUpVersionState.Draft))
        {
            if (new[] { "Markdown", "FileId", "Format", "Number", "WriteUpId", "ActorUserId", "SubmittedAt" }
                .Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("Submitted WriteUp content is immutable.");
        }
    }

    private void SynchronizeNormalizedSearchFields()
    {
        static string Normalize(string value) => value.Trim().ToUpperInvariant();
        foreach (var entry in ChangeTracker.Entries<User>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.NormalizedEmail = Normalize(entry.Entity.Email);
        foreach (var entry in ChangeTracker.Entries<ExternalIdentity>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.NormalizedSubject = Normalize(entry.Entity.Subject);
        foreach (var entry in ChangeTracker.Entries<Competition>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.NormalizedTitle = Normalize(entry.Entity.Title);
        foreach (var entry in ChangeTracker.Entries<Challenge>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.NormalizedTitle = Normalize(entry.Entity.Title);
            entry.Entity.NormalizedDirection = Normalize(entry.Entity.Direction);
        }

        foreach (var entry in ChangeTracker.Entries<CompetitionChallenge>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.NormalizedCustomTitle = entry.Entity.CustomTitle is null
                ? null : Normalize(entry.Entity.CustomTitle);
        foreach (var entry in ChangeTracker.Entries<Team>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.NormalizedName = Normalize(entry.Entity.Name);
    }

    private void SynchronizeTeamMemberships()
    {
        var changedMembershipTeams = ChangeTracker.Entries<TeamMember>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.TeamId).ToHashSet();
        foreach (var entry in ChangeTracker.Entries<Team>()
                     .Where(entry => entry.State is not (EntityState.Deleted or EntityState.Detached)))
        {
            var team = entry.Entity;
            if (entry.State == EntityState.Unchanged && !changedMembershipTeams.Contains(team.Id)
                && !entry.Collection(item => item.Members).IsLoaded)
                continue;
            if (entry.State == EntityState.Unchanged && changedMembershipTeams.Contains(team.Id))
                entry.Property(nameof(Team.ConcurrencyStamp)).IsModified = true;
            foreach (var member in team.Members)
            {
                member.TeamId = team.Id;
                member.CompetitionId = team.CompetitionId;
                if (team.DeletedAt is not null)
                    member.ActiveMembership = null;
                else
                    member.ActiveMembership ??= new ActiveTeamMembership
                    {
                        TeamId = team.Id, CompetitionId = team.CompetitionId, UserId = member.UserId
                    };
            }
            if (!team.Members.Any(member => member.UserId == team.CaptainId))
                throw new InvalidOperationException("A team captain must be a team member.");
            team.CaptainMembership ??= new TeamCaptain();
            team.CaptainMembership.TeamId = team.Id;
            team.CaptainMembership.UserId = team.CaptainId;
        }
    }

    private void SynchronizeTypedConfigurationForeignKeys()
    {
        foreach (var entry in ChangeTracker.Entries<Competition>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added && entry.Entity.Directions.Count == 0)
                entry.Entity.Directions = NoCTF.Domain.Competitions.Directions.CompetitionDirectionDefaults.Create(entry.Entity.Id);
            if (entry.State == EntityState.Added && entry.Entity.Tracks.Count == 0)
                entry.Entity.Tracks = CompetitionTrackConfiguration.ToPersisted(
                    CompetitionTrackConfiguration.DefaultFor(entry.Entity.Mode),
                    entry.Entity.Id);
            _ = CompetitionTrackConfiguration.FromPersisted(
                entry.Entity.Mode,
                entry.Entity.Tracks);
            for (var position = 0; position < entry.Entity.Tracks.Count; position++)
                entry.Entity.Tracks[position] = entry.Entity.Tracks[position] with
                {
                    CompetitionId = entry.Entity.Id,
                    Position = position
                };
            var configuration = entry.Entity.ModeConfiguration
                ?? throw new InvalidOperationException("Competition mode configuration is required.");
            if (configuration.Mode != entry.Entity.Mode)
                throw new InvalidOperationException(
                    "Competition mode configuration must match the competition mode.");
            configuration.CompetitionId = entry.Entity.Id;
        }

        foreach (var entry in ChangeTracker.Entries<Challenge>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var definition = entry.Entity.Definition
                ?? throw new InvalidOperationException("Challenge definition is required.");
            if (definition.Mode != entry.Entity.Mode)
                throw new InvalidOperationException(
                    "Challenge definition must match the challenge mode.");
            ChallengeDefinitionGraph.AssignChallengeId(definition, entry.Entity.Id);
        }

        foreach (var entry in ChangeTracker.Entries<CompetitionChallenge>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var rules = entry.Entity.Rules
                ?? throw new InvalidOperationException("Competition challenge rules are required.");
            if (rules.Mode != entry.Entity.Mode)
                throw new InvalidOperationException(
                    "Competition challenge rules must match the competition challenge mode.");
            rules.CompetitionChallengeId = entry.Entity.Id;
            foreach (var reward in rules.BloodRewards)
                reward.CompetitionChallengeId = entry.Entity.Id;
        }
    }

    private void SynchronizeChallengeFlagIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<ChallengeFlag>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var flag = entry.Entity;
            if ((flag.SpecificationKind is null) != (flag.SpecificationId is null))
                throw new InvalidOperationException(
                    "A challenge flag specification kind and id must be supplied together.");
            var validStructure = flag.Type switch
            {
                ChallengeFlagType.Template => flag.ChallengeId is not null
                    && flag.CompetitionChallengeId is null && flag.TeamId is null,
                ChallengeFlagType.Competition => flag.ChallengeId is null
                    && flag.CompetitionChallengeId is not null && flag.TeamId is null,
                ChallengeFlagType.Team => flag.ChallengeId is null
                    && flag.CompetitionChallengeId is not null && flag.TeamId is not null,
                ChallengeFlagType.AwdRound => flag.ChallengeId is null
                    && flag.CompetitionChallengeId is not null && flag.TeamId is not null
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.ValidStart is not null && flag.ValidUntil is not null,
                ChallengeFlagType.RuntimeInstance =>
                    (flag.ChallengeId is null) != (flag.CompetitionChallengeId is null)
                    && flag.SpecificationKind == SpecificationKind.RuntimeInstance,
                _ => false
            };
            if (!validStructure)
                throw new InvalidOperationException(
                    $"Challenge flag type {flag.Type} has an invalid relational scope.");
            flag.SpecificationIdentity = flag.SpecificationKind is null
                ? $"flag:{flag.Id:N}"
                : flag is TeamChallengeFlag
                    && flag.SpecificationKind == SpecificationKind.Attachment
                    ? string.Join(':',
                        $"competition-challenge-{flag.CompetitionChallengeId!.Value:N}",
                        $"team-{flag.TeamId!.Value:N}",
                        ((short)flag.SpecificationKind.Value).ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                    : string.Join(':',
                    flag.ChallengeId is Guid challengeId ? $"challenge-{challengeId:N}" : "challenge-none",
                    flag.CompetitionChallengeId is Guid competitionChallengeId
                        ? $"competition-challenge-{competitionChallengeId:N}"
                        : "competition-challenge-none",
                    flag.TeamId is Guid teamId ? $"team-{teamId:N}" : "team-none",
                    ((short)flag.SpecificationKind.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    flag.SpecificationId!.Value.ToString("N"));
        }
    }

    private void SynchronizeSsoProviderChildren()
    {
        foreach (var settingsEntry in ChangeTracker.Entries<PlatformSettings>())
        {
            foreach (var provider in settingsEntry.Entity.SsoProviders)
            {
                provider.PlatformSettingsId = settingsEntry.Entity.Id;
                var providerEntry = Entry(provider);
                if (providerEntry.State == EntityState.Detached)
                    providerEntry.State = EntityState.Added;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SsoProviderConfiguration>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var host in entry.Entity.AllowedHostEntries)
            {
                host.SsoProviderId = entry.Entity.Id;
                var hostEntry = Entry(host);
                if (hostEntry.State == EntityState.Detached)
                    hostEntry.State = EntityState.Added;
            }
            if (entry.Entity is OidcSsoProviderConfiguration oidc)
            {
                foreach (var scope in oidc.ScopeEntries)
                {
                    scope.SsoProviderId = oidc.Id;
                    var scopeEntry = Entry(scope);
                    if (scopeEntry.State == EntityState.Detached)
                        scopeEntry.State = EntityState.Added;
                }
            }
        }
    }

    private void ValidateExternalIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<ExternalIdentity>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var identity = entry.Entity;
            if (identity.UserId == Guid.Empty
                || identity.ProviderId == Guid.Empty
                || !Enum.IsDefined(identity.Protocol)
                || string.IsNullOrWhiteSpace(identity.IdentityNamespace)
                || string.IsNullOrWhiteSpace(identity.Subject)
                || identity.BoundAt == default)
            {
                throw new InvalidOperationException(
                    "An external identity binding must be complete.");
            }
        }
    }

    private void SynchronizeActiveRuntimeSlots()
    {
        var allRuntimeEntries = ChangeTracker.Entries<RuntimeInstance>().ToArray();
        var runtimeEntries = allRuntimeEntries
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();
        var slotEntries = ChangeTracker.Entries<ActiveRuntimeSlot>().ToArray();
        foreach (var entry in runtimeEntries.Where(entry => entry.Entity.State is not (
                     RuntimeState.Queued or RuntimeState.Provisioning
                     or RuntimeState.Running or RuntimeState.Stopping)))
            entry.Entity.ActiveSlot = null;

        var activeGroups = runtimeEntries
            .Where(entry => entry.Entity.State is RuntimeState.Queued
                or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping)
            .GroupBy(entry => ActiveRuntimeSlot.CreateKey(entry.Entity));
        foreach (var group in activeGroups)
        {
            var ownerEntry = group
                .Where(entry => entry.Entity.State != RuntimeState.Stopping)
                .OrderByDescending(entry => entry.Entity.CreatedAt)
                .ThenByDescending(entry => entry.Entity.Id)
                .FirstOrDefault()
                ?? group.OrderByDescending(entry => entry.Entity.CreatedAt)
                    .ThenByDescending(entry => entry.Entity.Id)
                    .First();
            foreach (var nonOwner in group.Where(entry => entry != ownerEntry))
                nonOwner.Entity.ActiveSlot = null;

            var instance = ownerEntry.Entity;
            if (instance.ActiveSlot is not null)
                continue;
            var key = group.Key;
            var reusable = slotEntries.FirstOrDefault(slot => slot.Entity.Key == key);
            if (reusable is not null)
            {
                var priorOwner = allRuntimeEntries.FirstOrDefault(runtime =>
                    runtime.Entity.Id == reusable.Entity.RuntimeInstanceId)?.Entity;
                if (priorOwner is not null
                    && priorOwner != instance
                    && priorOwner.State is not (RuntimeState.Stopping
                        or RuntimeState.Stopped or RuntimeState.Failed))
                {
                    throw new DbUpdateConcurrencyException(
                        $"Runtime slot '{key}' is owned by active runtime {priorOwner.Id}.");
                }
                reusable.State = EntityState.Modified;
                reusable.Entity.RuntimeInstanceId = instance.Id;
            }
            else
            {
                instance.ActiveSlot = new ActiveRuntimeSlot
                {
                    RuntimeInstanceId = instance.Id,
                    Key = key
                };
            }
        }
    }
}
