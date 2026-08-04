using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for all NoCTF business facts and current state.</summary>
public sealed class NoCtfDbContext(DbContextOptions<NoCtfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAccountLifecycleAudit> UserAccountLifecycleAudits =>
        Set<UserAccountLifecycleAudit>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<CompetitionChallenge> CompetitionChallenges => Set<CompetitionChallenge>();
    public DbSet<ChallengeFlag> ChallengeFlags => Set<ChallengeFlag>();
    public DbSet<RuntimeInstance> RuntimeInstances => Set<RuntimeInstance>();
    public DbSet<PatchUpload> PatchUploads => Set<PatchUpload>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<EmailVerificationSettings> EmailVerificationSettings =>
        Set<EmailVerificationSettings>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ScoringEvent> ScoringEvents => Set<ScoringEvent>();
    public DbSet<DurableMaintenanceSchedule> DurableMaintenanceSchedules =>
        Set<DurableMaintenanceSchedule>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoCtfDbContext).Assembly);
    }
}
