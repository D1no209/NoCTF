using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.DataExports;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Relational persistence for all NoCTF business facts and current state.</summary>
public sealed class NoCtfDbContext(DbContextOptions<NoCtfDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionEvent> CompetitionEvents => Set<CompetitionEvent>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<CompetitionChallenge> CompetitionChallenges => Set<CompetitionChallenge>();
    public DbSet<ChallengeFlag> ChallengeFlags => Set<ChallengeFlag>();
    public DbSet<RuntimeInstance> RuntimeInstances => Set<RuntimeInstance>();
    public DbSet<PatchUpload> PatchUploads => Set<PatchUpload>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<GameplayFact> GameplayFacts => Set<GameplayFact>();
    public DbSet<DataExport> DataExports => Set<DataExport>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NoCtfDbContext).Assembly);
        ApplyUtcDateTimeOffsetConversions(modelBuilder);
    }

    /// <summary>
    /// Npgsql only accepts offset-0 DateTimeOffset values for timestamptz columns. API payloads may carry
    /// local offsets (for example +08:00), so normalize every DateTimeOffset to UTC at the mapping layer;
    /// this also covers ExecuteUpdateAsync, which bypasses SaveChanges interception. Column types are
    /// unchanged, so this conversion produces no migration diff.
    /// </summary>
    private static void ApplyUtcDateTimeOffsetConversions(ModelBuilder modelBuilder)
    {
        var utcConverter = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(),
            value => value.ToUniversalTime());
        var utcNullableConverter = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            value => value.HasValue ? value.Value.ToUniversalTime() : value,
            value => value.HasValue ? value.Value.ToUniversalTime() : value);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
        {
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(utcConverter);
            else if (property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(utcNullableConverter);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnlyFacts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAppendOnlyFacts();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAppendOnlyFacts()
    {
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

        if (ChangeTracker.Entries<StoredFile>().Any(entry => entry.State == EntityState.Modified))
            throw new InvalidOperationException("Stored file metadata is immutable.");
    }
}
