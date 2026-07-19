using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Auditing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for all NoCTF business facts and current state.</summary>
public sealed class NoCtfDbContext(DbContextOptions<NoCtfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionConfiguration> CompetitionConfigurations => Set<CompetitionConfiguration>();
    public DbSet<CompetitionCollaborator> CompetitionCollaborators => Set<CompetitionCollaborator>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<TeamInvitation> TeamInvitations => Set<TeamInvitation>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeConfiguration> ChallengeConfigurations => Set<ChallengeConfiguration>();
    public DbSet<ChallengeHint> ChallengeHints => Set<ChallengeHint>();
    public DbSet<ChallengeAttachment> ChallengeAttachments => Set<ChallengeAttachment>();
    public DbSet<ChallengeFlag> ChallengeFlags => Set<ChallengeFlag>();
    public DbSet<ChallengeInstance> ChallengeInstances => Set<ChallengeInstance>();
    public DbSet<RuntimeOperation> RuntimeOperations => Set<RuntimeOperation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ScoringEvent> ScoringEvents => Set<ScoringEvent>();
    public DbSet<FixSubmissionRecord> FixSubmissionRecords => Set<FixSubmissionRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoCtfDbContext).Assembly);
}
