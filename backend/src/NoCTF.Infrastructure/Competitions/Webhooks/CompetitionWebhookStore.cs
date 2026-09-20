using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed class CompetitionWebhookStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    ICompetitionEventRecorder events,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : ICompetitionWebhookStore
{
    private static readonly TimeSpan PreviousSecretLifetime = TimeSpan.FromHours(24);

    public async Task<CompetitionWebhookTargetPage?> ListAsync(
        Guid competitionId,
        int offset,
        int limit,
        bool descending,
        CancellationToken cancellationToken)
    {
        var configuration = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => item.WebhookConfiguration)
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            return null;

        var ordered = descending
            ? configuration.Targets
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id)
            : configuration.Targets
                .OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.Id);
        var targets = ordered.Skip(offset).Take(limit).ToArray();
        return new(
            targets.Select(ToView).ToArray(),
            configuration.Targets.Count);
    }

    public async Task<CompetitionWebhookMutationResult> CreateAsync(
        CreateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken)
    {
        var validated = Validate(command.Name, command.EndpointUrl);
        if (validated.Failure is { } failure)
            return new(Failure: failure);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var competition = await LockCompetitionAsync(command.CompetitionId, cancellationToken);
        if (competition is null)
            return new(Failure: CompetitionWebhookMutationFailure.CompetitionNotFound);
        if (HasDuplicateEndpoint(competition.WebhookConfiguration, validated.EndpointUrl!, null))
            return new(Failure: CompetitionWebhookMutationFailure.DuplicateEndpoint);

        var id = Guid.CreateVersion7(command.Now);
        var signingSecret = GenerateSigningSecret();
        var target = new CompetitionWebhookTarget
        {
            Id = id,
            Name = validated.Name!,
            EndpointUrl = validated.EndpointUrl!,
            Enabled = command.Enabled,
            EnabledAt = command.Enabled ? command.Now : null,
            CurrentSecretCiphertext = secrets.Protect(
                signingSecret,
                PlatformSecretPurpose.CompetitionWebhookSecret,
                command.CompetitionId,
                id),
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        };
        competition.WebhookConfiguration.Targets.Add(target);
        competition.UpdatedAt = command.Now;
        await RecordChangeAsync(competition.Id, "WebhookTargetCreated", command.Now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(ToView(target), signingSecret);
    }

    public async Task<CompetitionWebhookMutationResult> UpdateAsync(
        UpdateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken)
    {
        var validated = Validate(command.Name, command.EndpointUrl);
        if (validated.Failure is { } failure)
            return new(Failure: failure);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var competition = await LockCompetitionAsync(command.CompetitionId, cancellationToken);
        if (competition is null)
            return new(Failure: CompetitionWebhookMutationFailure.CompetitionNotFound);
        var target = competition.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == command.TargetId);
        if (target is null)
            return new(Failure: CompetitionWebhookMutationFailure.TargetNotFound);
        if (HasDuplicateEndpoint(
                competition.WebhookConfiguration,
                validated.EndpointUrl!,
                command.TargetId))
            return new(Failure: CompetitionWebhookMutationFailure.DuplicateEndpoint);

        var endpointChanged = !string.Equals(
            target.EndpointUrl,
            validated.EndpointUrl,
            StringComparison.Ordinal);
        var enabledNow = command.Enabled && (!target.Enabled || endpointChanged);
        target.Name = validated.Name!;
        target.EndpointUrl = validated.EndpointUrl!;
        target.Enabled = command.Enabled;
        target.EnabledAt = command.Enabled
            ? enabledNow ? command.Now : target.EnabledAt
            : null;
        target.DisabledReason = null;
        target.UpdatedAt = command.Now;
        competition.UpdatedAt = command.Now;
        await RecordChangeAsync(competition.Id, "WebhookTargetUpdated", command.Now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(ToView(target));
    }

    public async Task<CompetitionWebhookMutationResult> RotateSecretAsync(
        Guid competitionId,
        Guid targetId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var competition = await LockCompetitionAsync(competitionId, cancellationToken);
        if (competition is null)
            return new(Failure: CompetitionWebhookMutationFailure.CompetitionNotFound);
        var target = competition.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == targetId);
        if (target is null)
            return new(Failure: CompetitionWebhookMutationFailure.TargetNotFound);

        var signingSecret = GenerateSigningSecret();
        target.PreviousSecretCiphertext = target.CurrentSecretCiphertext;
        target.PreviousSecretValidUntil = now + PreviousSecretLifetime;
        target.CurrentSecretCiphertext = secrets.Protect(
            signingSecret,
            PlatformSecretPurpose.CompetitionWebhookSecret,
            competitionId,
            targetId);
        target.UpdatedAt = now;
        competition.UpdatedAt = now;
        await RecordChangeAsync(competition.Id, "WebhookSecretRotated", now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(ToView(target), signingSecret);
    }

    public async Task<CompetitionWebhookMutationFailure?> DeleteAsync(
        Guid competitionId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var competition = await LockCompetitionAsync(competitionId, cancellationToken);
        if (competition is null)
            return CompetitionWebhookMutationFailure.CompetitionNotFound;
        var removed = competition.WebhookConfiguration.Targets.RemoveAll(item => item.Id == targetId);
        if (removed == 0)
            return CompetitionWebhookMutationFailure.TargetNotFound;

        var now = timeProvider.GetUtcNow();
        competition.UpdatedAt = now;
        await RecordChangeAsync(competition.Id, "WebhookTargetDeleted", now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return null;
    }

    private Task<Competition?> LockCompetitionAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? db.Competitions.FromSqlInterpolated(
                    $"SELECT * FROM competitions WHERE id = {competitionId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
            : db.Competitions.SingleOrDefaultAsync(
                item => item.Id == competitionId,
                cancellationToken);

    private async Task RecordChangeAsync(
        Guid competitionId,
        string reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        _ = await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            occurredAt,
            Reason: reason), cancellationToken);

    private static CompetitionWebhookTargetView ToView(CompetitionWebhookTarget target) => new(
        target.Id,
        target.Name,
        target.EndpointUrl,
        target.Enabled,
        target.EnabledAt,
        target.CurrentSecretCiphertext.Length > 0,
        target.PreviousSecretValidUntil,
        target.DisabledReason,
        target.CreatedAt,
        target.UpdatedAt);

    private static bool HasDuplicateEndpoint(
        CompetitionWebhookConfiguration configuration,
        string endpointUrl,
        Guid? exceptTargetId) =>
        configuration.Targets.Any(item => item.Id != exceptTargetId
            && string.Equals(item.EndpointUrl, endpointUrl, StringComparison.OrdinalIgnoreCase));

    private static ValidationResult Validate(string name, string endpointUrl)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100)
            return new(Failure: CompetitionWebhookMutationFailure.InvalidName);
        if (endpointUrl.Length > 2048
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return new(Failure: CompetitionWebhookMutationFailure.InvalidEndpoint);
        }
        return new(name, uri.AbsoluteUri);
    }

    private static string GenerateSigningSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return "whsec_" + Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private sealed record ValidationResult(
        string? Name = null,
        string? EndpointUrl = null,
        CompetitionWebhookMutationFailure? Failure = null);
}
