using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Management;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ForceDeleteCompetitionRequest
{
    public string ConfirmationTitle { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ForceDeleteCompetitionValidator
    : Validator<ForceDeleteCompetitionRequest>
{
    public ForceDeleteCompetitionValidator()
    {
        RuleFor(request => request.ConfirmationTitle).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(500);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionForceDeleteConflictCode>))]
public enum CompetitionForceDeleteConflictCode
{
    ActiveCompetition,
    ActiveRuntimeResource,
    ConfirmationMismatch,
    NotificationScopeConflict
}

public sealed record CompetitionForceDeleteConflictResponse(
    CompetitionForceDeleteConflictCode Code,
    string Detail,
    CompetitionHardDeletePreviewResponse? Preview = null,
    IReadOnlyList<Guid>? ConflictingNotificationIds = null);

public sealed class ForceDeleteCompetitionEndpoint(
    ForceDeleteCompetition forceDelete,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ForceDeleteCompetitionRequest,
        Results<NoContent, NotFound, Conflict<CompetitionForceDeleteConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/force-delete");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminForceDeleteCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes a competition and all scoped data.";
            summary.Description =
                "Requires the exact competition title and an audit reason. Running or paused competitions and competitions with live Runtime resources are rejected.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, Conflict<CompetitionForceDeleteConflictResponse>, ProblemHttpResult>>
        ExecuteAsync(ForceDeleteCompetitionRequest request, CancellationToken ct)
    {
        var result = await forceDelete.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            user.UserId,
            request.ConfirmationTitle,
            request.Reason,
            timeProvider.GetUtcNow()), user.IsAdministrator, ct);
        return result.State switch
        {
            CompetitionForceDeleteState.Deleted => TypedResults.NoContent(),
            CompetitionForceDeleteState.NotFound => TypedResults.NotFound(),
            CompetitionForceDeleteState.ActiveCompetition => TypedResults.Conflict<CompetitionForceDeleteConflictResponse>(new(
                CompetitionForceDeleteConflictCode.ActiveCompetition,
                "Finish the competition before permanently deleting it. Paused competitions cannot be deleted.",
                result.Preview is null ? null : CompetitionHardDeleteMapping.ToResponse(result.Preview))),
            CompetitionForceDeleteState.ActiveRuntimeResource => TypedResults.Conflict<CompetitionForceDeleteConflictResponse>(new(
                CompetitionForceDeleteConflictCode.ActiveRuntimeResource,
                "One or more runtime resources are still active. Terminate them and wait for cleanup before retrying.",
                result.Preview is null ? null : CompetitionHardDeleteMapping.ToResponse(result.Preview))),
            CompetitionForceDeleteState.ConfirmationMismatch => TypedResults.Conflict<CompetitionForceDeleteConflictResponse>(new(
                CompetitionForceDeleteConflictCode.ConfirmationMismatch,
                "The confirmation title does not exactly match the current competition title.")),
            CompetitionForceDeleteState.NotificationScopeConflict => TypedResults.Conflict<CompetitionForceDeleteConflictResponse>(new(
                CompetitionForceDeleteConflictCode.NotificationScopeConflict,
                "Notification threads contain cross-scope or unproven references. No data was deleted. Review the conflicting notification IDs before retrying.",
                result.Preview is null ? null : CompetitionHardDeleteMapping.ToResponse(result.Preview),
                result.ConflictingNotificationIds)),
            CompetitionForceDeleteState.InvalidReason => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Competition was not force-deleted.",
                detail: "Reason must contain between 8 and 500 characters."),
            _ => throw new InvalidOperationException(
                $"Unsupported force-delete state: {result.State}.")
        };
    }
}
