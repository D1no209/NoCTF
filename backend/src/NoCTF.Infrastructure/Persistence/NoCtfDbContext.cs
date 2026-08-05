using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for all NoCTF business facts and current state.</summary>
public sealed class NoCtfDbContext(DbContextOptions<NoCtfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAccountLifecycleAudit> UserAccountLifecycleAudits =>
        Set<UserAccountLifecycleAudit>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionEvent> CompetitionEvents => Set<CompetitionEvent>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<CompetitionChallenge> CompetitionChallenges => Set<CompetitionChallenge>();
    public DbSet<CompetitionQuestion> CompetitionQuestions => Set<CompetitionQuestion>();
    public DbSet<ChallengeFlag> ChallengeFlags => Set<ChallengeFlag>();
    public DbSet<RuntimeInstance> RuntimeInstances => Set<RuntimeInstance>();
    public DbSet<PatchUpload> PatchUploads => Set<PatchUpload>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureCompetitionEventsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureCompetitionEventsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureCompetitionEventsAreAppendOnly()
    {
        if (ChangeTracker.Entries<CompetitionEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Competition events are immutable and cannot be updated or deleted.");
        }
    }
}
