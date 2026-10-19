namespace NoCTF.Application.Authentication.Account;

public sealed record NavigationCapabilities(bool CanAccessTheoryBank, bool HasManagedCompetitions);

public interface INavigationCapabilitiesReader
{
    Task<NavigationCapabilities?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class GetNavigationCapabilities(INavigationCapabilitiesReader reader)
{
    public Task<NavigationCapabilities?> ExecuteAsync(Guid userId, CancellationToken ct) => reader.ReadAsync(userId, ct);
}
