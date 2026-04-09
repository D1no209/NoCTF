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
    }

    private static void SetTenantQueryFilter<TEntity>(ModelBuilder builder, ITenantContext tenantContext)
        where TEntity : class, ITenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(e => e.CompetitionId == tenantContext.CompetitionId);
    }
}
