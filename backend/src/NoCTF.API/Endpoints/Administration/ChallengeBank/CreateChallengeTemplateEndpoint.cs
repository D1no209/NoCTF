using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeVisibilityProtocol>))]
public enum ChallengeVisibilityProtocol
{
    Private,
    Shared
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SpecificationKindProtocol>))]
public enum SpecificationKindProtocol
{
    Attachment,
    AwdRound,
    RuntimeDefinition,
    Hint,
    RuntimeInstance
}

public sealed class CreateChallengeTemplateRequest
{
    public Guid? Id { get; set; }
    public GameModeProtocol Mode { get; set; }
    public ChallengeVisibilityProtocol Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
}

public sealed record ChallengeTemplateResponse(
    Guid Id,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    GameModeProtocol Mode,
    ChallengeVisibilityProtocol Visibility,
    string Title,
    string? Description,
    string Direction,
    string DefinitionJson,
    DateTimeOffset? DeletedAt,
    int ActiveCompetitionReferenceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ChallengeTemplateConflictCode>))]
public enum ChallengeTemplateConflictCode
{
    ResourceIdConflict,
    ActiveCompetitionModeConflict,
    OwnerIncludedInManagerSet,
    UserNotFound,
    RoleNotEligible
}

public sealed record ChallengeTemplateConflictResponse(
    [property: Required, JsonRequired] ChallengeTemplateConflictCode Code,
    [property: Required, JsonRequired] IReadOnlyList<Guid> UserIds);

internal static class ChallengeTemplateWriteResponseMapper
{
    public static ChallengeTemplateConflictResponse ToConflict(
        ChallengeTemplateWriteResult result) =>
        new(
            result.State switch
            {
                ChallengeTemplateWriteState.ResourceIdConflict =>
                    ChallengeTemplateConflictCode.ResourceIdConflict,
                ChallengeTemplateWriteState.ActiveCompetitionModeConflict =>
                    ChallengeTemplateConflictCode.ActiveCompetitionModeConflict,
                ChallengeTemplateWriteState.OwnerIncludedInManagerSet =>
                    ChallengeTemplateConflictCode.OwnerIncludedInManagerSet,
                ChallengeTemplateWriteState.UserNotFound =>
                    ChallengeTemplateConflictCode.UserNotFound,
                ChallengeTemplateWriteState.RoleNotEligible =>
                    ChallengeTemplateConflictCode.RoleNotEligible,
                _ => throw new InvalidOperationException(
                    $"Unsupported challenge template conflict state: {result.State}.")
            },
            result.UserIds ?? []);
}

public sealed class CreateChallengeTemplateValidator : Validator<CreateChallengeTemplateRequest>
{
    public CreateChallengeTemplateValidator()
    {
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
    }
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class ChallengeTemplateMapper
{
    public static CreateChallengeTemplateCommand ToCommand(
        CreateChallengeTemplateRequest request,
        Guid ownerId,
        DateTimeOffset createdAt) =>
        new(
            request.Id,
            ownerId,
            CompetitionProtocolMapper.ToDomain(request.Mode),
            ToDomain(request.Visibility),
            request.Title,
            request.Description,
            request.Direction,
            request.DefinitionJson,
            createdAt);
    public static partial ChallengeTemplateResponse ToResponse(ChallengeTemplateView source);
    private static partial IReadOnlyList<ChallengeTemplateResponse> ToResponses(
        IReadOnlyList<ChallengeTemplateView> source);
    public static ChallengeTemplateListResponse ToListResponse(IReadOnlyList<ChallengeTemplateView> source) =>
        new(ToResponses(source));

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ChallengeVisibility ToDomain(ChallengeVisibilityProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ChallengeVisibilityProtocol ToProtocol(ChallengeVisibility value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SpecificationKind ToDomain(SpecificationKindProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SpecificationKindProtocol ToProtocol(SpecificationKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial GameModeProtocol ToProtocol(GameMode value);
}

public sealed class CreateChallengeTemplateEndpoint(
    CreateChallengeTemplate create,
    IUserContext user)
    : Endpoint<
        CreateChallengeTemplateRequest,
        Results<
            Created<ChallengeTemplateResponse>,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankCreateTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Creates a global challenge template.";
            summary.Description = "Creates a reusable question-bank template independent of any competition.";
        });
    }

    public override async Task<
        Results<
            Created<ChallengeTemplateResponse>,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        CreateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var result = await create.ExecuteAsync(
            ChallengeTemplateMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow),
            ct);
        return result.State switch
        {
            ChallengeTemplateWriteState.Succeeded =>
                TypedResults.Created(
                    $"/api/v1/admin/challenges/{result.Template!.Id}",
                    ChallengeTemplateMapper.ToResponse(result.Template)),
            ChallengeTemplateWriteState.InvalidRequest
                or ChallengeTemplateWriteState.InvalidDefinition =>
                TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Challenge template was not created.",
                detail: result.Detail),
            ChallengeTemplateWriteState.ResourceIdConflict
                or ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template creation state: {result.State}.")
        };
    }
}
