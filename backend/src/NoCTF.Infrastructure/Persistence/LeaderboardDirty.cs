namespace NoCTF.Infrastructure.Persistence;

public static class LeaderboardDirty
{
    public static Task MarkAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken ct)
        => Task.CompletedTask;
}
