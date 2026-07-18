using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Auditing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for current state; event history is owned by Marten.</summary>
public sealed class NoCtfDbContext(DbContextOptions<NoCtfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionConfiguration> CompetitionConfigurations => Set<CompetitionConfiguration>();
    public DbSet<CompetitionCollaborator> CompetitionCollaborators => Set<CompetitionCollaborator>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeConfiguration> ChallengeConfigurations => Set<ChallengeConfiguration>();
    public DbSet<ChallengeHint> ChallengeHints => Set<ChallengeHint>();
    public DbSet<ChallengeAttachment> ChallengeAttachments => Set<ChallengeAttachment>();
    public DbSet<ChallengeInstance> ChallengeInstances => Set<ChallengeInstance>();
    public DbSet<RuntimeOperation> RuntimeOperations => Set<RuntimeOperation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoCtfDbContext).Assembly);
}
