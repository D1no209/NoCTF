using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Images;

public enum ChallengeImageLocationKind
{
    RuntimeContainer,
    RuntimeComposeService,
    Checker
}

public sealed record ChallengeImageLocation(
    ChallengeImageLocationKind Kind,
    string? ServiceName = null);

public sealed record ChallengeImageReference(
    ChallengeImageLocation Location,
    string Image);

public sealed record ChallengeImageDefinitionReadResult(
    IReadOnlyList<ChallengeImageReference>? Images,
    string? Error = null)
{
    public bool Succeeded => Images is not null;
}

public interface IChallengeImageDefinitionCatalog
{
    ChallengeImageDefinitionReadResult Read(GameMode mode, string definitionJson);

    string Replace(
        GameMode mode,
        string definitionJson,
        IReadOnlyDictionary<ChallengeImageLocation, string> replacements);
}

public sealed record ContainerImageReference(
    string Name,
    string RegistryHost,
    string Repository,
    string Reference,
    bool IsDigest)
{
    private static readonly Regex RepositoryPattern = new(
        "^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TagPattern = new(
        "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new(
        "^sha256:[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public string Pinned(string digest) => $"{Name}@{digest}";

    public static bool TryParse(string value, out ContainerImageReference reference)
    {
        reference = null!;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 512
            || value.Contains("://", StringComparison.Ordinal)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            return false;

        var at = value.LastIndexOf('@');
        string name;
        string imageReference;
        var isDigest = at >= 0;
        if (isDigest)
        {
            if (at == 0
                || at != value.IndexOf('@')
                || !DigestPattern.IsMatch(value[(at + 1)..]))
                return false;
            name = value[..at];
            imageReference = value[(at + 1)..];
        }
        else
        {
            var lastSlash = value.LastIndexOf('/');
            var lastColon = value.LastIndexOf(':');
            if (lastColon > lastSlash)
            {
                name = value[..lastColon];
                imageReference = value[(lastColon + 1)..];
                if (!TagPattern.IsMatch(imageReference))
                    return false;
            }
            else
            {
                name = value;
                imageReference = "latest";
            }
        }

        if (string.IsNullOrWhiteSpace(name) || name.Contains('@', StringComparison.Ordinal))
            return false;
        var parts = name.Split('/', StringSplitOptions.None);
        if (parts.Any(string.IsNullOrWhiteSpace))
            return false;
        var first = parts[0];
        var hasRegistry = first.Contains('.', StringComparison.Ordinal)
            || first.Contains(':', StringComparison.Ordinal)
            || string.Equals(first, "localhost", StringComparison.OrdinalIgnoreCase);
        var registry = hasRegistry ? first : "registry-1.docker.io";
        if (string.Equals(registry, "docker.io", StringComparison.OrdinalIgnoreCase))
            registry = "registry-1.docker.io";
        var repository = hasRegistry
            ? string.Join('/', parts.Skip(1))
            : string.Join('/', parts);
        if (!hasRegistry && !repository.Contains('/', StringComparison.Ordinal))
            repository = $"library/{repository}";
        if (string.IsNullOrWhiteSpace(repository)
            || !RepositoryPattern.IsMatch(repository))
            return false;
        if (!Uri.TryCreate($"https://{registry}", UriKind.Absolute, out var registryUri)
            || string.IsNullOrWhiteSpace(registryUri.Host)
            || registryUri.UserInfo.Length > 0
            || registryUri.Query.Length > 0
            || registryUri.Fragment.Length > 0
            || registryUri.AbsolutePath != "/")
            return false;

        reference = new(name, registry, repository, imageReference, isDigest);
        return true;
    }
}

public static class ChallengeImagePinningPolicy
{
    public static bool IsPinnedImage(string image) =>
        ContainerImageReference.TryParse(image, out var parsed) && parsed.IsDigest;

    public static bool AreRuntimeImagesPinned(ChallengeRuntimeTemplate template)
    {
        try
        {
            return template.Definition switch
            {
                ContainerRuntimeDefinition container => IsPinnedImage(container.Image),
                ComposeRuntimeDefinition compose =>
                    AreComposeImagesPinned(compose.ComposeYaml),
                _ => true
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool AreComposeImagesPinned(string composeYaml)
    {
        try
        {
            return ComposeRuntimeDefinitionPolicy
                .ReadServiceImages(composeYaml)
                .Values
                .All(IsPinnedImage);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public enum RegistryManifestFailureCode
{
    InvalidImageReference,
    AuthenticationRequired,
    AuthenticationFailed,
    RegistryUnavailable,
    ManifestNotFound,
    ManifestInvalid
}

public sealed record RegistryManifestResolution(
    string? PinnedImage,
    RegistryManifestFailureCode? Failure = null,
    string? Detail = null)
{
    public bool Succeeded => PinnedImage is not null;
}

public interface IContainerRegistryManifestResolver
{
    Task<RegistryManifestResolution> ResolveAsync(
        string image,
        CancellationToken cancellationToken);
}

public sealed record ChallengeImagePinSnapshot(
    Guid ChallengeId,
    GameMode Mode,
    string DefinitionJson,
    int Revision);

public sealed record CompetitionImagePinSnapshot(
    bool CompetitionExists,
    IReadOnlyList<ChallengeImagePinSnapshot> Challenges);

public sealed record CompetitionImagePublicationBoundary(
    bool CompetitionExists,
    CompetitionStatus? Status,
    IReadOnlyList<ChallengeImagePinSnapshot> Challenges);

public sealed record ChallengeImagePinPlan(
    ChallengeImagePinSnapshot Source,
    string PinnedDefinitionJson);

public sealed record ChallengeTemplateWriteBoundary(
    bool ChallengeExists,
    bool RequiresPinnedDefinition);

public enum ChallengeImagePinCommitState
{
    Succeeded,
    ChallengeNotFound,
    RevisionConflict
}

public interface IChallengeImagePinningStore
{
    Task<ChallengeImagePinSnapshot?> LoadChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken);

    Task<ChallengeImagePinSnapshot?> LoadCompetitionChallengeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken);

    Task<CompetitionImagePinSnapshot> LoadCompetitionAsync(
        Guid competitionId,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task<ChallengeImagePinCommitState> ApplyAsync(
        IReadOnlyList<ChallengeImagePinPlan> plans,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<bool> RequiresPinnedDefinitionAsync(
        Guid challengeId,
        CancellationToken cancellationToken);

    Task<ChallengeTemplateWriteBoundary> LockTemplateWriteBoundaryAsync(
        Guid challengeId,
        CancellationToken cancellationToken);

    Task<CompetitionImagePublicationBoundary> LockCompetitionPublicationBoundaryAsync(
        Guid competitionId,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChallengeImagePinSnapshot>> LockCompetitionAsync(
        Guid competitionId,
        CancellationToken cancellationToken);

    Task<ChallengeImagePinSnapshot?> LockCompetitionChallengeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken);
}

public enum ChallengeImagePinFailureCode
{
    CompetitionNotFound,
    ChallengeNotFound,
    InvalidDefinition,
    InvalidImageReference,
    RegistryAuthenticationRequired,
    RegistryAuthenticationFailed,
    RegistryUnavailable,
    RegistryManifestNotFound,
    RegistryManifestInvalid,
    RevisionConflict
}

public sealed record ChallengeImagePinError(
    ChallengeImagePinFailureCode Code,
    Guid? ChallengeId,
    ChallengeImageLocation? Location,
    string Message);

public sealed record ChallengeImagePinResult(
    IReadOnlyList<ChallengeImagePinError> Errors,
    IReadOnlyDictionary<Guid, int>? ChallengeRevisions = null)
{
    public bool Succeeded => Errors.Count == 0;

    public static ChallengeImagePinResult Success { get; } = new(
        [],
        new Dictionary<Guid, int>());
}

public sealed class PinChallengeImages(
    IChallengeImagePinningStore store,
    IChallengeImageDefinitionCatalog definitions,
    IContainerRegistryManifestResolver registries)
{
    private const int MaxConcurrentRegistryResolutions = 8;
    private static readonly TimeSpan ResolutionBudget = TimeSpan.FromSeconds(60);

    public async Task<ChallengeImagePinResult> PinChallengeAsync(
        Guid challengeId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var snapshot = await store.LoadChallengeAsync(challengeId, ct);
        return snapshot is null
            ? Failure(
                ChallengeImagePinFailureCode.ChallengeNotFound,
                challengeId,
                null,
                "Challenge was not found.")
            : await PinAsync([snapshot], now, ct);
    }

    public async Task<ChallengeImagePinResult> PinCompetitionChallengeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var snapshot = await store.LoadCompetitionChallengeAsync(
            competitionId,
            competitionChallengeId,
            ct);
        return snapshot is null
            ? Failure(
                ChallengeImagePinFailureCode.ChallengeNotFound,
                null,
                null,
                "Competition challenge or its template was not found.")
            : await PinAsync([snapshot], now, ct);
    }

    public async Task<ChallengeImagePinResult> PinCompetitionAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct = default,
        bool includeDeleted = false)
    {
        var snapshot = await store.LoadCompetitionAsync(
            competitionId,
            includeDeleted,
            ct);
        if (!snapshot.CompetitionExists)
        {
            return Failure(
                ChallengeImagePinFailureCode.CompetitionNotFound,
                null,
                null,
                "Competition was not found.");
        }
        return await PinAsync(snapshot.Challenges, now, ct);
    }

    private async Task<ChallengeImagePinResult> PinAsync(
        IReadOnlyList<ChallengeImagePinSnapshot> snapshots,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var reads = new List<(ChallengeImagePinSnapshot Snapshot, ChallengeImageDefinitionReadResult Definition)>();
        var errors = new List<ChallengeImagePinError>();
        foreach (var snapshot in snapshots.DistinctBy(item => item.ChallengeId))
        {
            var definition = definitions.Read(snapshot.Mode, snapshot.DefinitionJson);
            if (!definition.Succeeded)
            {
                errors.Add(new(
                    ChallengeImagePinFailureCode.InvalidDefinition,
                    snapshot.ChallengeId,
                    null,
                    definition.Error ?? "Challenge definition is invalid."));
                continue;
            }
            reads.Add((snapshot, definition));
        }
        if (errors.Count > 0)
            return new(errors);

        var images = reads
            .SelectMany(item => item.Definition.Images!)
            .Select(item => item.Image)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolved = new ConcurrentDictionary<string, RegistryManifestResolution>(
            StringComparer.Ordinal);
        using var resolutionBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        resolutionBudget.CancelAfter(ResolutionBudget);
        try
        {
            await Parallel.ForEachAsync(
                images,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentRegistryResolutions,
                    CancellationToken = resolutionBudget.Token
                },
                async (image, cancellationToken) =>
                {
                    resolved[image] = await registries.ResolveAsync(
                        image,
                        cancellationToken);
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(
                ChallengeImagePinFailureCode.RegistryUnavailable,
                null,
                null,
                "Container image resolution exceeded the publication time budget.");
        }

        var plans = new List<ChallengeImagePinPlan>();
        foreach (var (snapshot, definition) in reads)
        {
            var replacements = new Dictionary<ChallengeImageLocation, string>();
            foreach (var image in definition.Images!)
            {
                var resolution = resolved[image.Image];
                if (!resolution.Succeeded)
                {
                    errors.Add(new(
                        Map(resolution.Failure!.Value),
                        snapshot.ChallengeId,
                        image.Location,
                        resolution.Detail ?? "Container image could not be resolved."));
                    continue;
                }
                replacements[image.Location] = resolution.PinnedImage!;
            }
            if (errors.Count > 0)
                continue;
            var pinned = definitions.Replace(snapshot.Mode, snapshot.DefinitionJson, replacements);
            if (!string.Equals(pinned, snapshot.DefinitionJson, StringComparison.Ordinal))
                plans.Add(new(snapshot, pinned));
        }
        if (errors.Count > 0)
            return new(errors);
        var changedChallengeIds = plans
            .Select(plan => plan.Source.ChallengeId)
            .ToHashSet();
        var pinnedRevisions = reads.ToDictionary(
            item => item.Snapshot.ChallengeId,
            item => changedChallengeIds.Contains(item.Snapshot.ChallengeId)
                ? checked(item.Snapshot.Revision + 1)
                : item.Snapshot.Revision);
        if (plans.Count == 0)
            return new([], pinnedRevisions);

        return await store.ApplyAsync(plans, now, ct) switch
        {
            ChallengeImagePinCommitState.Succeeded => new([], pinnedRevisions),
            ChallengeImagePinCommitState.ChallengeNotFound => Failure(
                ChallengeImagePinFailureCode.ChallengeNotFound,
                null,
                null,
                "Challenge was deleted while its images were being resolved."),
            _ => Failure(
                ChallengeImagePinFailureCode.RevisionConflict,
                null,
                null,
                "Challenge definition changed while its images were being resolved.")
        };
    }

    private static ChallengeImagePinFailureCode Map(RegistryManifestFailureCode failure) =>
        failure switch
        {
            RegistryManifestFailureCode.InvalidImageReference =>
                ChallengeImagePinFailureCode.InvalidImageReference,
            RegistryManifestFailureCode.AuthenticationRequired =>
                ChallengeImagePinFailureCode.RegistryAuthenticationRequired,
            RegistryManifestFailureCode.AuthenticationFailed =>
                ChallengeImagePinFailureCode.RegistryAuthenticationFailed,
            RegistryManifestFailureCode.RegistryUnavailable =>
                ChallengeImagePinFailureCode.RegistryUnavailable,
            RegistryManifestFailureCode.ManifestNotFound =>
                ChallengeImagePinFailureCode.RegistryManifestNotFound,
            _ => ChallengeImagePinFailureCode.RegistryManifestInvalid
        };

    private static ChallengeImagePinResult Failure(
        ChallengeImagePinFailureCode code,
        Guid? challengeId,
        ChallengeImageLocation? location,
        string message) =>
        new([new(code, challengeId, location, message)]);
}
