using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionHardDeleteReferenceCode>))]
public enum CompetitionHardDeleteReferenceCode
{
    HistoricalEvent,
    Team,
    CompetitionChallenge,
    GameplayFact,
    RuntimeInstance,
    PatchUpload,
    DataExport,
    Notification,
    PosterFile,
    ActiveRuntimeResource
}

public sealed record CompetitionHardDeleteReferenceResponse(
    CompetitionHardDeleteReferenceCode Code,
    int Count);

public sealed record CompetitionHardDeletePreviewResponse(
    Guid CompetitionId,
    string Title,
    bool IsSoftDeleted,
    bool CanHardDelete,
    bool CanForceDelete,
    IReadOnlyList<CompetitionHardDeleteReferenceResponse> References);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class CompetitionHardDeleteMapping
{
    public static partial CompetitionHardDeletePreviewResponse ToResponse(
        CompetitionHardDeletePreview preview);

    [MapProperty(
        nameof(CompetitionHardDeleteReference.Kind),
        nameof(CompetitionHardDeleteReferenceResponse.Code))]
    public static partial CompetitionHardDeleteReferenceResponse ToResponse(
        CompetitionHardDeleteReference reference);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial CompetitionHardDeleteReferenceCode ToProtocol(
        CompetitionHardDeleteReferenceKind value);
}

public sealed class PreviewCompetitionHardDeleteEndpoint(
    PreviewCompetitionHardDelete previewHardDelete,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<CompetitionHardDeletePreviewResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/hard-delete-preview");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPreviewCompetitionHardDelete"));
        Summary(summary =>
        {
            summary.Summary = "Previews the impact of permanently deleting a competition.";
            summary.Description =
                "Reports immutable historical events and other references that prevent physical deletion. The preview does not modify the competition.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionHardDeletePreviewResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var preview = await previewHardDelete.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return preview is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionHardDeleteMapping.ToResponse(preview));
    }
}
