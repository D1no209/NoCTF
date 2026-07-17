using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;

namespace NoCTF.Infrastructure;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<EmailVerificationSettings> EmailVerificationSettings => Set<EmailVerificationSettings>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionCollaborator> CompetitionCollaborators => Set<CompetitionCollaborator>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeTemplate> ChallengeTemplates => Set<ChallengeTemplate>();
    public DbSet<ChallengeHint> ChallengeHints => Set<ChallengeHint>();
    public DbSet<CtfDynamicFlag> CtfDynamicFlags => Set<CtfDynamicFlag>();
    public DbSet<PenetrationTopologyTemplate> PenetrationTopologyTemplates => Set<PenetrationTopologyTemplate>();
    public DbSet<PenetrationNodeTemplate> PenetrationNodeTemplates => Set<PenetrationNodeTemplate>();
    public DbSet<PenetrationFlagTemplate> PenetrationFlagTemplates => Set<PenetrationFlagTemplate>();
    public DbSet<PenetrationTopology> PenetrationTopologies => Set<PenetrationTopology>();
    public DbSet<PenetrationNode> PenetrationNodes => Set<PenetrationNode>();
    public DbSet<PenetrationFlag> PenetrationFlags => Set<PenetrationFlag>();
    public DbSet<TeamChallengeInstance> TeamChallengeInstances => Set<TeamChallengeInstance>();
    public DbSet<DynamicFlagInstance> DynamicFlagInstances => Set<DynamicFlagInstance>();
    public DbSet<CompetitionLog> CompetitionLogs => Set<CompetitionLog>();
    public DbSet<CheatIncident> CheatIncidents => Set<CheatIncident>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ScoreEvent> ScoreEvents => Set<ScoreEvent>();
    public DbSet<ScoreSignal> ScoreSignals => Set<ScoreSignal>();
    public DbSet<AwdRound> AwdRounds => Set<AwdRound>();
    public DbSet<AwdAttackRecord> AwdAttackRecords => Set<AwdAttackRecord>();
    public DbSet<AwdFlag> AwdFlags => Set<AwdFlag>();
    public DbSet<AwdGameBox> AwdGameBoxes => Set<AwdGameBox>();
    public DbSet<AwdCheckResult> AwdCheckResults => Set<AwdCheckResult>();
    public DbSet<AwdpRound> AwdpRounds => Set<AwdpRound>();
    public DbSet<AwdpTeamChallengeState> AwdpTeamChallengeStates => Set<AwdpTeamChallengeState>();
    public DbSet<AwdpRoundScore> AwdpRoundScores => Set<AwdpRoundScore>();
    public DbSet<AwdpPatchSubmission> AwdpPatchSubmissions => Set<AwdpPatchSubmission>();
    public DbSet<KohControlRecord> KohControlRecords => Set<KohControlRecord>();
    public DbSet<BackgroundTaskItem> BackgroundTasks => Set<BackgroundTaskItem>();
    public DbSet<StorageCleanupItem> StorageCleanupItems => Set<StorageCleanupItem>();
    public DbSet<CompetitionEngineState> CompetitionEngineStates => Set<CompetitionEngineState>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<QqBotGlobalSettings> QqBotGlobalSettings => Set<QqBotGlobalSettings>();
    public DbSet<QqBotAgent> QqBotAgents => Set<QqBotAgent>();
    public DbSet<QqBotGroup> QqBotGroups => Set<QqBotGroup>();
    public DbSet<CompetitionQqBotSettings> CompetitionQqBotSettings => Set<CompetitionQqBotSettings>();
    public DbSet<CompetitionQqBotEventRule> CompetitionQqBotEventRules => Set<CompetitionQqBotEventRule>();
    public DbSet<CompetitionQqBotGroupBinding> CompetitionQqBotGroupBindings => Set<CompetitionQqBotGroupBinding>();
    public DbSet<QqBotTemplate> QqBotTemplates => Set<QqBotTemplate>();
    public DbSet<QqBotEvent> QqBotEvents => Set<QqBotEvent>();
    public DbSet<QqBotDelivery> QqBotDeliveries => Set<QqBotDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType)))
        {
            var method = typeof(ApplicationDbContext)
                .GetMethod(nameof(SetTenantQueryFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder, tenantContext]);
        }

        modelBuilder.Entity<Challenge>()
            .OwnsOne(c => c.PointsConfig);

        modelBuilder.Entity<Challenge>()
            .OwnsOne(c => c.CheckerConfig);

        modelBuilder.Entity<ChallengeTemplate>()
            .OwnsOne(c => c.CheckerConfig);

        modelBuilder.Entity<ChallengeTemplate>()
            .OwnsOne(c => c.KohAgentConfig);

        modelBuilder.Entity<EmailVerificationToken>()
            .Property(token => token.TokenHash)
            .HasMaxLength(64);

        modelBuilder.Entity<EmailVerificationToken>()
            .HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_emailverificationtokens_hash");

        modelBuilder.Entity<EmailVerificationToken>()
            .HasIndex(token => new { token.UserId, token.CreatedAt })
            .HasDatabaseName("ix_emailverificationtokens_user_created");

        modelBuilder.Entity<EmailVerificationToken>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EmailVerificationSettings>()
            .Property(settings => settings.SmtpPasswordProtected)
            .HasMaxLength(2048);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId })
            .HasDatabaseName("ix_submissions_competition_team");

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId, s.ChallengeId, s.SubmittedAt })
            .HasDatabaseName("ix_submissions_rate_limit_window");

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.ChallengeId, s.SubmittedAt })
            .HasFilter("\"IsCorrect\" = true")
            .HasDatabaseName("ix_submissions_correct_challenge_time");

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId, s.ChallengeId })
            .IsUnique()
            .HasFilter("\"IsCorrect\" = true AND \"PenetrationFlagId\" IS NULL")
            .HasDatabaseName("ux_submissions_correct_once");

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId, s.ChallengeId, s.PenetrationFlagId })
            .IsUnique()
            .HasFilter("\"IsCorrect\" = true AND \"PenetrationFlagId\" IS NOT NULL")
            .HasDatabaseName("ux_submissions_penetration_flag_correct_once");

        modelBuilder.Entity<ScoreEvent>()
            .HasIndex(se => new { se.CompetitionId, se.TeamId })
            .HasDatabaseName("ix_scoreevents_competition_team");

        modelBuilder.Entity<ScoreEvent>()
            .HasIndex(se => new { se.CompetitionId, se.ScoringKey, se.ChallengeId, se.TeamId })
            .HasDatabaseName("ix_scoreevents_competition_scoring_challenge_team");

        modelBuilder.Entity<ScoreEvent>()
            .HasIndex(se => new { se.CompetitionId, se.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" <> ''")
            .HasDatabaseName("ux_scoreevents_competition_idempotency");

        modelBuilder.Entity<ScoreSignal>()
            .HasIndex(s => new { s.CompetitionId, s.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_scoresignals_competition_idempotency");

        modelBuilder.Entity<ScoreSignal>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId, s.SignalType })
            .HasDatabaseName("ix_scoresignals_competition_team_type");

        modelBuilder.Entity<ScoreSignal>()
            .HasIndex(s => new { s.CompetitionId, s.SignalType, s.SubjectId, s.TeamId })
            .HasDatabaseName("ix_scoresignals_competition_type_subject_team");

        modelBuilder.Entity<Challenge>()
            .HasIndex(c => c.CompetitionId)
            .HasDatabaseName("ix_challenges_competition");

        modelBuilder.Entity<Challenge>()
            .HasIndex(c => new { c.CompetitionId, c.TemplateId })
            .HasDatabaseName("ix_challenges_competition_template");

        modelBuilder.Entity<Challenge>()
            .HasIndex(c => c.AttachmentStorageKey)
            .HasMethod("hash")
            .HasFilter("\"AttachmentStorageKey\" IS NOT NULL")
            .HasDatabaseName("ix_challenges_attachment_storage_key");

        modelBuilder.Entity<Challenge>()
            .HasIndex(c => c.PatchTemplateStorageKey)
            .HasMethod("hash")
            .HasFilter("\"PatchTemplateStorageKey\" IS NOT NULL")
            .HasDatabaseName("ix_challenges_patch_template_storage_key");

        modelBuilder.Entity<ChallengeTemplate>()
            .HasIndex(c => c.Title)
            .HasDatabaseName("ix_challenge_templates_title");

        modelBuilder.Entity<ChallengeTemplate>()
            .HasIndex(c => c.AttachmentStorageKey)
            .HasMethod("hash")
            .HasFilter("\"AttachmentStorageKey\" IS NOT NULL")
            .HasDatabaseName("ix_challenge_templates_attachment_storage_key");

        modelBuilder.Entity<ChallengeTemplate>()
            .HasIndex(c => c.PatchTemplateStorageKey)
            .HasMethod("hash")
            .HasFilter("\"PatchTemplateStorageKey\" IS NOT NULL")
            .HasDatabaseName("ix_challenge_templates_patch_template_storage_key");

        modelBuilder.Entity<ChallengeHint>()
            .HasIndex(h => new { h.CompetitionId, h.ChallengeId, h.DisplayOrder })
            .HasDatabaseName("ix_challengehints_competition_challenge_order");

        modelBuilder.Entity<CtfDynamicFlag>()
            .HasIndex(f => new { f.CompetitionId, f.TeamId, f.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ux_ctfdynamicflags_competition_team_challenge");

        modelBuilder.Entity<CtfDynamicFlag>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.FlagUuid })
            .IsUnique()
            .HasDatabaseName("ux_ctfdynamicflags_competition_challenge_uuid");

        modelBuilder.Entity<PenetrationTopologyTemplate>()
            .HasIndex(t => t.ChallengeTemplateId)
            .IsUnique()
            .HasDatabaseName("ux_pentopologytemplates_challenge_template");

        modelBuilder.Entity<PenetrationNodeTemplate>()
            .HasIndex(n => new { n.TopologyTemplateId, n.Name })
            .IsUnique()
            .HasDatabaseName("ux_pennodetemplates_topology_name");

        modelBuilder.Entity<PenetrationFlagTemplate>()
            .HasIndex(f => new { f.TopologyTemplateId, f.Stage })
            .IsUnique()
            .HasDatabaseName("ux_penflagtemplates_topology_stage");

        modelBuilder.Entity<PenetrationTopology>()
            .HasIndex(t => new { t.CompetitionId, t.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ux_pentopologies_competition_challenge");

        modelBuilder.Entity<PenetrationNode>()
            .HasIndex(n => new { n.CompetitionId, n.TopologyId })
            .HasDatabaseName("ix_pennodes_competition_topology");

        modelBuilder.Entity<PenetrationNode>()
            .HasIndex(n => new { n.CompetitionId, n.TopologyId, n.Name })
            .IsUnique()
            .HasDatabaseName("ux_pennodes_competition_topology_name");

        modelBuilder.Entity<PenetrationFlag>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.Stage })
            .IsUnique()
            .HasDatabaseName("ux_penflags_competition_challenge_stage");

        modelBuilder.Entity<PenetrationFlag>()
            .HasIndex(f => new { f.CompetitionId, f.TopologyId })
            .HasDatabaseName("ix_penflags_competition_topology");

        modelBuilder.Entity<PenetrationFlag>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.ValueHash })
            .HasFilter("\"ValueHash\" IS NOT NULL")
            .HasDatabaseName("ix_penflags_competition_challenge_value_hash");

        modelBuilder.Entity<TeamChallengeInstance>()
            .HasIndex(i => new { i.CompetitionId, i.TeamId, i.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ux_teamchallengeinstances_competition_team_challenge");

        modelBuilder.Entity<TeamChallengeInstance>()
            .HasIndex(i => new { i.CompetitionId, i.Status })
            .HasDatabaseName("ix_teamchallengeinstances_competition_status");

        modelBuilder.Entity<TeamChallengeInstance>()
            .HasIndex(i => new { i.ExpiresAt, i.Status })
            .HasFilter("\"ExpiresAt\" IS NOT NULL")
            .HasDatabaseName("ix_teamchallengeinstances_expiry_status");

        modelBuilder.Entity<TeamChallengeInstance>()
            .HasIndex(i => new { i.Status, i.UpdatedAt, i.LastActionAt })
            .HasDatabaseName("ix_teamchallengeinstances_status_activity");

        modelBuilder.Entity<DynamicFlagInstance>()
            .HasIndex(f => new { f.CompetitionId, f.TeamId, f.FlagId, f.IsActive })
            .HasDatabaseName("ix_dynamicflaginstances_competition_team_flag_active");

        modelBuilder.Entity<DynamicFlagInstance>()
            .HasIndex(f => new { f.CompetitionId, f.TeamId, f.FlagId })
            .IsUnique()
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("ux_dynamicflaginstances_active_team_flag");

        modelBuilder.Entity<DynamicFlagInstance>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.FlagId })
            .HasDatabaseName("ix_dynamicflaginstances_competition_challenge_flag");

        modelBuilder.Entity<DynamicFlagInstance>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.ValueHash })
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("ix_dynamicflaginstances_active_challenge_hash");

        modelBuilder.Entity<CompetitionLog>()
            .HasIndex(l => new { l.CompetitionId, l.CreatedAt })
            .HasDatabaseName("ix_competitionlogs_competition_created");

        modelBuilder.Entity<CompetitionLog>()
            .HasIndex(l => new { l.CompetitionId, l.EventType })
            .HasDatabaseName("ix_competitionlogs_competition_event");

        modelBuilder.Entity<CheatIncident>()
            .HasIndex(i => new { i.CompetitionId, i.CreatedAt })
            .HasDatabaseName("ix_cheatincidents_competition_created");

        modelBuilder.Entity<CheatIncident>()
            .HasIndex(i => new { i.CompetitionId, i.SuspectTeamId })
            .HasDatabaseName("ix_cheatincidents_competition_suspect");

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.CompetitionId)
            .HasDatabaseName("ix_teams_competition");

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.InviteToken)
            .IsUnique()
            .HasDatabaseName("ux_teams_invite_token");

        modelBuilder.Entity<Team>()
            .HasIndex(t => new { t.CompetitionId, t.RegistrationStatus })
            .HasDatabaseName("ix_teams_competition_registration_status");

        modelBuilder.Entity<Team>()
            .HasIndex(t => new { t.CompetitionId, t.IsBanned })
            .HasDatabaseName("ix_teams_competition_banned");

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.TeamId, tm.UserId })
            .IsUnique()
            .HasDatabaseName("ix_teammembers_team_user");

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.CompetitionId, tm.UserId })
            .IsUnique()
            .HasDatabaseName("ix_teammembers_competition_user");

        modelBuilder.Entity<CompetitionCollaborator>()
            .HasIndex(cc => new { cc.CompetitionId, cc.UserId })
            .IsUnique()
            .HasDatabaseName("ix_competitioncollaborators_competition_user");

        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .HasMaxLength(UserInputLimits.EmailMaxLength);

        modelBuilder.Entity<User>()
            .Property(u => u.UserName)
            .HasMaxLength(UserInputLimits.UserNameMaxLength);

        modelBuilder.Entity<User>()
            .Property<string>("NormalizedEmail")
            .HasMaxLength(UserInputLimits.EmailMaxLength)
            .HasComputedColumnSql("lower(btrim(\"Email\"))", stored: true);

        modelBuilder.Entity<User>()
            .Property<string>("NormalizedUserName")
            .HasMaxLength(UserInputLimits.UserNameMaxLength)
            .HasComputedColumnSql("lower(btrim(\"UserName\"))", stored: true);

        modelBuilder.Entity<User>()
            .HasIndex("NormalizedEmail")
            .IsUnique()
            .HasDatabaseName("ix_users_email");

        modelBuilder.Entity<User>()
            .HasIndex("NormalizedUserName")
            .IsUnique()
            .HasDatabaseName("ix_users_username");

        modelBuilder.Entity<UserNotification>()
            .Property(notification => notification.Type)
            .HasMaxLength(64);

        modelBuilder.Entity<UserNotification>()
            .Property(notification => notification.DataJson)
            .HasMaxLength(4096);

        modelBuilder.Entity<UserNotification>()
            .Property(notification => notification.IdempotencyKey)
            .HasMaxLength(512);

        modelBuilder.Entity<UserNotification>()
            .HasIndex(notification => new { notification.UserId, notification.CreatedAt })
            .HasDatabaseName("ix_usernotifications_user_created");

        modelBuilder.Entity<UserNotification>()
            .HasIndex(notification => new { notification.UserId, notification.IsRead, notification.CreatedAt })
            .HasDatabaseName("ix_usernotifications_user_read_created");

        modelBuilder.Entity<UserNotification>()
            .HasIndex(notification => new { notification.UserId, notification.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_usernotifications_user_idempotency");

        modelBuilder.Entity<Competition>()
            .HasIndex(c => new { c.Status, c.StartTime })
            .HasDatabaseName("ix_competitions_status_start");

        modelBuilder.Entity<AwdRound>()
            .HasIndex(r => new { r.CompetitionId, r.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ux_awdrounds_competition_round");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => a.CompetitionId)
            .HasDatabaseName("ix_awdattackrecords_competition");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => new { a.CompetitionId, a.RoundNumber })
            .HasDatabaseName("ix_awdattackrecords_competition_round");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => new { a.CompetitionId, a.AttackerTeamId, a.VictimTeamId, a.ChallengeId, a.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ix_awdattackrecords_unique_attack");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => new { a.CompetitionId, a.RoundNumber, a.VictimTeamId, a.ChallengeId })
            .HasDatabaseName("ix_awdattackrecords_round_victim_challenge");

        modelBuilder.Entity<AwdFlag>()
            .HasIndex(f => f.CompetitionId)
            .HasDatabaseName("ix_awdflags_competition");

        modelBuilder.Entity<AwdFlag>()
            .HasIndex(f => new { f.CompetitionId, f.TeamId, f.ChallengeId, f.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ix_awdflags_competition_team_challenge_round");

        modelBuilder.Entity<AwdFlag>()
            .HasIndex(f => new { f.CompetitionId, f.ChallengeId, f.FlagContent })
            .HasDatabaseName("ix_awdflags_competition_challenge_content");

        modelBuilder.Entity<AwdGameBox>()
            .HasIndex(g => g.CompetitionId)
            .HasDatabaseName("ix_awdgameboxes_competition");

        modelBuilder.Entity<AwdGameBox>()
            .HasIndex(g => new { g.CompetitionId, g.TeamId, g.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ix_awdgameboxes_competition_team_challenge");

        modelBuilder.Entity<AwdGameBox>()
            .HasIndex(g => new { g.ExpiresAt, g.CleanupLockedUntil })
            .HasFilter("\"ContainerInstanceId\" IS NOT NULL AND \"ExpiresAt\" IS NOT NULL")
            .HasDatabaseName("ix_awdgameboxes_expired_instances");

        modelBuilder.Entity<AwdGameBox>()
            .Property(g => g.ContainerInstanceId)
            .IsConcurrencyToken();

        modelBuilder.Entity<AwdGameBox>()
            .Property(g => g.RuntimeOperationId)
            .IsConcurrencyToken();

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_awdcheckresults_competition");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => new { r.CompetitionId, r.RoundNumber })
            .HasDatabaseName("ix_awdcheckresults_competition_round");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => new { r.CompetitionId, r.TeamId, r.ChallengeId, r.RoundNumber })
            .HasDatabaseName("ix_awdcheckresults_competition_team_challenge_round");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => new { r.CompetitionId, r.RoundNumber, r.TeamId, r.ChallengeId, r.CheckedAt })
            .HasDatabaseName("ix_awdcheckresults_round_team_challenge_checked");

        modelBuilder.Entity<AwdpRound>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_awdprounds_competition");

        modelBuilder.Entity<AwdpRound>()
            .HasIndex(r => new { r.CompetitionId, r.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ux_awdprounds_competition_round");

        modelBuilder.Entity<AwdpTeamChallengeState>()
            .HasIndex(s => s.CompetitionId)
            .HasDatabaseName("ix_awdpteamchallengestates_competition");

        modelBuilder.Entity<AwdpTeamChallengeState>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId, s.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ux_awdpteamchallengestates_competition_team_challenge");

        modelBuilder.Entity<AwdpRoundScore>()
            .HasIndex(s => s.CompetitionId)
            .HasDatabaseName("ix_awdproundscores_competition");

        modelBuilder.Entity<AwdpRoundScore>()
            .HasIndex(s => new { s.CompetitionId, s.RoundNumber, s.TeamId, s.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ux_awdproundscores_competition_round_team_challenge");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => p.CompetitionId)
            .HasDatabaseName("ix_awdpatchsubmissions_competition");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => new { p.CompetitionId, p.TeamId, p.ChallengeId })
            .HasDatabaseName("ix_awdpatchsubmissions_competition_team_challenge");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => new { p.CompetitionId, p.TeamId, p.SubmittedAt })
            .HasDatabaseName("ix_awdpatchsubmissions_competition_team_submitted");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => p.PatchArchiveUrl)
            .HasMethod("hash")
            .HasFilter("\"PatchArchiveUrl\" <> ''")
            .HasDatabaseName("ix_awdpatchsubmissions_patch_archive_key");

        modelBuilder.Entity<Challenge>()
            .OwnsOne(c => c.KohAgentConfig);

        modelBuilder.Entity<KohControlRecord>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_kohcontrolrecords_competition");

        modelBuilder.Entity<KohControlRecord>()
            .HasIndex(r => new { r.CompetitionId, r.ChallengeId, r.StartTime })
            .HasDatabaseName("ix_kohcontrolrecords_competition_challenge_start");

        modelBuilder.Entity<KohControlRecord>()
            .HasIndex(r => new { r.CompetitionId, r.ChallengeId })
            .IsUnique()
            .HasFilter("\"EndTime\" IS NULL")
            .HasDatabaseName("ux_kohcontrolrecords_active_hill");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(log => log.Timestamp)
            .HasDatabaseName("ix_auditlogs_timestamp");

        modelBuilder.Entity<BackgroundTaskItem>()
            .HasIndex(t => new { t.Status, t.LockedUntil, t.CreatedAt })
            .HasDatabaseName("ix_backgroundtasks_dispatch");

        modelBuilder.Entity<BackgroundTaskItem>()
            .HasIndex(t => new { t.Status, t.Type, t.LockedUntil, t.CreatedAt })
            .HasFilter("\"Status\" IN (0, 4)")
            .HasDatabaseName("ix_backgroundtasks_typed_dispatch");

        modelBuilder.Entity<BackgroundTaskItem>()
            .HasIndex(t => new { t.Status, t.UpdatedAt, t.LockedUntil })
            .HasFilter("\"Status\" = 1")
            .HasDatabaseName("ix_backgroundtasks_recovery");

        modelBuilder.Entity<BackgroundTaskItem>()
            .HasIndex(t => new { t.CompetitionId, t.Type })
            .HasDatabaseName("ix_backgroundtasks_competition_type");

        modelBuilder.Entity<StorageCleanupItem>()
            .Property(item => item.StorageKey)
            .HasMaxLength(1024);

        modelBuilder.Entity<StorageCleanupItem>()
            .HasIndex(item => item.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_storagecleanupitems_storage_key");

        modelBuilder.Entity<StorageCleanupItem>()
            .HasIndex(item => new { item.NotBefore, item.LockedUntil })
            .HasDatabaseName("ix_storagecleanupitems_dispatch");

        modelBuilder.Entity<CompetitionEngineState>()
            .HasIndex(s => new { s.CompetitionId, s.EngineKey })
            .IsUnique()
            .HasDatabaseName("ux_competitionenginestates_competition_engine");

        modelBuilder.Entity<QqBotAgent>()
            .Property(agent => agent.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<QqBotAgent>()
            .Property(agent => agent.PublicKeyPem)
            .HasMaxLength(4096);

        modelBuilder.Entity<QqBotAgent>()
            .Property(agent => agent.PreviousPublicKeyPem)
            .HasMaxLength(4096);

        modelBuilder.Entity<QqBotGroup>()
            .HasOne<QqBotAgent>()
            .WithMany()
            .HasForeignKey(group => group.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QqBotGroup>()
            .HasIndex(group => new { group.AgentId, group.GroupId })
            .IsUnique()
            .HasDatabaseName("ux_qqbotgroups_agent_group");

        modelBuilder.Entity<QqBotGroup>()
            .Property(group => group.GroupName)
            .HasMaxLength(256);

        modelBuilder.Entity<CompetitionQqBotSettings>()
            .HasIndex(settings => settings.CompetitionId)
            .IsUnique()
            .HasDatabaseName("ux_competitionqqbotsettings_competition");

        modelBuilder.Entity<CompetitionQqBotEventRule>()
            .HasIndex(rule => new { rule.CompetitionId, rule.EventType })
            .IsUnique()
            .HasDatabaseName("ux_competitionqqboteventrules_competition_event");

        modelBuilder.Entity<CompetitionQqBotEventRule>()
            .HasOne<QqBotTemplate>()
            .WithMany()
            .HasForeignKey(rule => rule.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CompetitionQqBotGroupBinding>()
            .HasIndex(binding => new { binding.CompetitionId, binding.GroupId })
            .IsUnique()
            .HasDatabaseName("ux_competitionqqbotgroupbindings_competition_group");

        modelBuilder.Entity<CompetitionQqBotGroupBinding>()
            .HasOne<QqBotAgent>()
            .WithMany()
            .HasForeignKey(binding => binding.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetitionQqBotGroupBinding>()
            .HasOne<QqBotGroup>()
            .WithMany()
            .HasForeignKey(binding => binding.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompetitionQqBotGroupBinding>()
            .Property(binding => binding.EventTypesJson)
            .HasMaxLength(1024);

        modelBuilder.Entity<QqBotTemplate>()
            .HasOne<Competition>()
            .WithMany()
            .HasForeignKey(template => template.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QqBotTemplate>()
            .HasIndex(template => new { template.CompetitionId, template.EventType, template.Name })
            .IsUnique()
            .HasDatabaseName("ux_qqbottemplates_scope_event_name");

        modelBuilder.Entity<QqBotTemplate>()
            .Property(template => template.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<QqBotTemplate>()
            .Property(template => template.Content)
            .HasMaxLength(8000);

        modelBuilder.Entity<QqBotEvent>()
            .HasIndex(botEvent => new { botEvent.Status, botEvent.CreatedAt })
            .HasDatabaseName("ix_qqbotevents_status_created");

        modelBuilder.Entity<QqBotEvent>()
            .HasIndex(botEvent => new { botEvent.CompetitionId, botEvent.IdempotencyKey })
            .HasDatabaseName("ix_qqbotevents_competition_idempotency");

        modelBuilder.Entity<QqBotEvent>()
            .Property(botEvent => botEvent.IdempotencyKey)
            .HasMaxLength(512);

        modelBuilder.Entity<QqBotEvent>()
            .Property(botEvent => botEvent.SubjectType)
            .HasMaxLength(128);

        modelBuilder.Entity<QqBotDelivery>()
            .HasOne<QqBotEvent>()
            .WithMany()
            .HasForeignKey(delivery => delivery.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<QqBotDelivery>()
            .HasOne<QqBotAgent>()
            .WithMany()
            .HasForeignKey(delivery => delivery.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<QqBotDelivery>()
            .HasOne<QqBotGroup>()
            .WithMany()
            .HasForeignKey(delivery => delivery.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<QqBotDelivery>()
            .HasIndex(delivery => delivery.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_qqbotdeliveries_idempotency");

        modelBuilder.Entity<QqBotDelivery>()
            .HasIndex(delivery => new { delivery.AgentId, delivery.Status, delivery.AvailableAt, delivery.LockedUntil })
            .HasDatabaseName("ix_qqbotdeliveries_agent_dispatch");

        modelBuilder.Entity<QqBotDelivery>()
            .HasIndex(delivery => new { delivery.CompetitionId, delivery.CreatedAt })
            .HasDatabaseName("ix_qqbotdeliveries_competition_created");

        modelBuilder.Entity<QqBotDelivery>()
            .Property(delivery => delivery.IdempotencyKey)
            .HasMaxLength(768);

        modelBuilder.Entity<QqBotDelivery>()
            .Property(delivery => delivery.MessageDigest)
            .HasMaxLength(128);

        modelBuilder.Entity<TeamChallengeInstance>()
            .Property(i => i.UpdatedAt)
            .IsConcurrencyToken();

        ConfigureCompetitionOwnership<Team>(modelBuilder);
        ConfigureCompetitionOwnership<TeamMember>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionCollaborator>(modelBuilder);
        ConfigureCompetitionOwnership<Challenge>(modelBuilder);
        ConfigureCompetitionOwnership<ChallengeHint>(modelBuilder);
        ConfigureCompetitionOwnership<CtfDynamicFlag>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionLog>(modelBuilder);
        ConfigureCompetitionOwnership<CheatIncident>(modelBuilder);
        ConfigureCompetitionOwnership<Submission>(modelBuilder);
        ConfigureCompetitionOwnership<PenetrationTopology>(modelBuilder);
        ConfigureCompetitionOwnership<PenetrationNode>(modelBuilder);
        ConfigureCompetitionOwnership<PenetrationFlag>(modelBuilder);
        ConfigureCompetitionOwnership<TeamChallengeInstance>(modelBuilder);
        ConfigureCompetitionOwnership<DynamicFlagInstance>(modelBuilder);
        ConfigureCompetitionOwnership<ScoreEvent>(modelBuilder);
        ConfigureCompetitionOwnership<ScoreSignal>(modelBuilder);
        ConfigureCompetitionOwnership<AwdRound>(modelBuilder);
        ConfigureCompetitionOwnership<AwdAttackRecord>(modelBuilder);
        ConfigureCompetitionOwnership<AwdFlag>(modelBuilder);
        ConfigureCompetitionOwnership<AwdGameBox>(modelBuilder);
        ConfigureCompetitionOwnership<AwdCheckResult>(modelBuilder);
        ConfigureCompetitionOwnership<AwdpRound>(modelBuilder);
        ConfigureCompetitionOwnership<AwdpTeamChallengeState>(modelBuilder);
        ConfigureCompetitionOwnership<AwdpRoundScore>(modelBuilder);
        ConfigureCompetitionOwnership<AwdpPatchSubmission>(modelBuilder);
        ConfigureCompetitionOwnership<KohControlRecord>(modelBuilder);
        ConfigureCompetitionOwnership<BackgroundTaskItem>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionEngineState>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionQqBotSettings>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionQqBotEventRule>(modelBuilder);
        ConfigureCompetitionOwnership<CompetitionQqBotGroupBinding>(modelBuilder);
        ConfigureCompetitionOwnership<QqBotEvent>(modelBuilder);
        ConfigureCompetitionOwnership<QqBotDelivery>(modelBuilder);
    }

    private static void SetTenantQueryFilter<TEntity>(ModelBuilder builder, ITenantContext tenantContext)
        where TEntity : class, ITenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(e => e.CompetitionId == tenantContext.CompetitionId);
    }

    private static void ConfigureCompetitionOwnership<TEntity>(ModelBuilder builder)
        where TEntity : class
    {
        builder.Entity<TEntity>()
            .HasOne<Competition>()
            .WithMany()
            .HasForeignKey("CompetitionId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
