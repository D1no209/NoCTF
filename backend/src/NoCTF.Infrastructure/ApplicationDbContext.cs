using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;

namespace NoCTF.Infrastructure;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionCollaborator> CompetitionCollaborators => Set<CompetitionCollaborator>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ScoreEvent> ScoreEvents => Set<ScoreEvent>();
    public DbSet<AwdRound> AwdRounds => Set<AwdRound>();
    public DbSet<AwdAttackRecord> AwdAttackRecords => Set<AwdAttackRecord>();
    public DbSet<AwdFlag> AwdFlags => Set<AwdFlag>();
    public DbSet<AwdGameBox> AwdGameBoxes => Set<AwdGameBox>();
    public DbSet<AwdCheckResult> AwdCheckResults => Set<AwdCheckResult>();
    public DbSet<AwdpPatchSubmission> AwdpPatchSubmissions => Set<AwdpPatchSubmission>();
    public DbSet<KohControlRecord> KohControlRecords => Set<KohControlRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.CompetitionId, s.TeamId })
            .HasDatabaseName("ix_submissions_competition_team");

        modelBuilder.Entity<ScoreEvent>()
            .HasIndex(se => new { se.CompetitionId, se.TeamId })
            .HasDatabaseName("ix_scoreevents_competition_team");

        modelBuilder.Entity<Challenge>()
            .HasIndex(c => c.CompetitionId)
            .HasDatabaseName("ix_challenges_competition");

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.CompetitionId)
            .HasDatabaseName("ix_teams_competition");

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.TeamId, tm.UserId })
            .IsUnique()
            .HasDatabaseName("ix_teammembers_team_user");

        modelBuilder.Entity<CompetitionCollaborator>()
            .HasIndex(cc => new { cc.CompetitionId, cc.UserId })
            .IsUnique()
            .HasDatabaseName("ix_competitioncollaborators_competition_user");

        modelBuilder.Entity<AwdRound>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_awdrounds_competition");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => a.CompetitionId)
            .HasDatabaseName("ix_awdattackrecords_competition");

        modelBuilder.Entity<AwdAttackRecord>()
            .HasIndex(a => new { a.CompetitionId, a.RoundNumber })
            .HasDatabaseName("ix_awdattackrecords_competition_round");

        modelBuilder.Entity<AwdFlag>()
            .HasIndex(f => f.CompetitionId)
            .HasDatabaseName("ix_awdflags_competition");

        modelBuilder.Entity<AwdFlag>()
            .HasIndex(f => new { f.CompetitionId, f.TeamId, f.ChallengeId, f.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ix_awdflags_competition_team_challenge_round");

        modelBuilder.Entity<AwdGameBox>()
            .HasIndex(g => g.CompetitionId)
            .HasDatabaseName("ix_awdgameboxes_competition");

        modelBuilder.Entity<AwdGameBox>()
            .HasIndex(g => new { g.CompetitionId, g.TeamId, g.ChallengeId })
            .IsUnique()
            .HasDatabaseName("ix_awdgameboxes_competition_team_challenge");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_awdcheckresults_competition");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => new { r.CompetitionId, r.RoundNumber })
            .HasDatabaseName("ix_awdcheckresults_competition_round");

        modelBuilder.Entity<AwdCheckResult>()
            .HasIndex(r => new { r.CompetitionId, r.TeamId, r.ChallengeId, r.RoundNumber })
            .HasDatabaseName("ix_awdcheckresults_competition_team_challenge_round");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => p.CompetitionId)
            .HasDatabaseName("ix_awdpatchsubmissions_competition");

        modelBuilder.Entity<AwdpPatchSubmission>()
            .HasIndex(p => new { p.CompetitionId, p.TeamId, p.ChallengeId })
            .HasDatabaseName("ix_awdpatchsubmissions_competition_team_challenge");

        modelBuilder.Entity<Challenge>()
            .OwnsOne(c => c.KohAgentConfig);

        modelBuilder.Entity<KohControlRecord>()
            .HasIndex(r => r.CompetitionId)
            .HasDatabaseName("ix_kohcontrolrecords_competition");

        modelBuilder.Entity<KohControlRecord>()
            .HasIndex(r => new { r.CompetitionId, r.ChallengeId })
            .HasDatabaseName("ix_kohcontrolrecords_competition_challenge");
    }

    private static void SetTenantQueryFilter<TEntity>(ModelBuilder builder, ITenantContext tenantContext)
        where TEntity : class, ITenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(e => e.CompetitionId == tenantContext.CompetitionId);
    }
}
