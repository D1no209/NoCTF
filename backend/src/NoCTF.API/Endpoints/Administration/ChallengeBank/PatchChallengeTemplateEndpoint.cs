using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Common;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class ChallengeTemplateContentPatchRequest
{
    public required GameModeProtocol Mode { get; set; }
    public required ChallengeVisibilityProtocol Visibility { get; set; }
    public required string Title { get; set; }
    public required string? Description { get; set; }
    public required string Direction { get; set; }
    public required ChallengeDefinitionContract Definition { get; set; }
}

public sealed class ChallengeTemplatePermissionsPatchRequest
{
    public required Guid OwnerId { get; set; }
    public required Guid[] ManagerIds { get; set; }
}

public sealed class PatchChallengeTemplateRequest
{
    public ChallengeTemplateContentPatchRequest? Content { get; set; }
    public ChallengeTemplatePermissionsPatchRequest? Permissions { get; set; }
}

[Flags]
internal enum ChallengeTemplatePatchSection
{
    None = 0,
    Content = 1 << 0,
    Permissions = 1 << 1
}

public sealed class PatchChallengeTemplateValidator
    : Validator<PatchChallengeTemplateRequest>
{
    public PatchChallengeTemplateValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Content is not null || request.Permissions is not null)
            .WithMessage("At least one challenge-template section is required.");
        RuleFor(request => request.Content!.Mode).IsInEnum()
            .When(request => request.Content is not null);
        RuleFor(request => request.Content!.Visibility).IsInEnum()
            .When(request => request.Content is not null);
        RuleFor(request => request.Content!.Title).NotEmpty().MaximumLength(160)
            .When(request => request.Content is not null);
        RuleFor(request => request.Content!.Direction).NotEmpty().MaximumLength(96)
            .When(request => request.Content is not null);
        RuleFor(request => request.Content!.Definition).NotNull()
            .When(request => request.Content is not null);
        RuleFor(request => request.Content!.Definition)
            .Must((request, definition) =>
                ChallengeDefinitionContractMapper.HasValidShape(definition)
                && definition!.Mode == request.Content!.Mode)
            .When(request => request.Content is not null)
            .WithMessage("Definition must contain exactly the branch matching the challenge mode.");
        RuleFor(request => request.Permissions!.OwnerId).NotEmpty()
            .When(request => request.Permissions is not null);
        RuleFor(request => request.Permissions!.ManagerIds).NotNull()
            .When(request => request.Permissions is not null);
        RuleForEach(request => request.Permissions!.ManagerIds).NotEmpty()
            .When(request => request.Permissions is not null);
    }
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both,
    UseDeepCloning = true)]
public static partial class ChallengeTemplatePatchMapper
{
    [MapperIgnoreTarget(nameof(Challenge.Id))]
    [MapperIgnoreTarget(nameof(Challenge.Managers))]
    [MapperIgnoreTarget(nameof(Challenge.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(Challenge.OwnerId))]
    [MapperIgnoreTarget(nameof(Challenge.ManagerIds))]
    [MapperIgnoreTarget(nameof(Challenge.CreatedAt))]
    [MapperIgnoreTarget(nameof(Challenge.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Challenge.DeletedAt))]
    [MapperIgnoreTarget(nameof(Challenge.Attachments))]
    [MapperIgnoreTarget(nameof(Challenge.Definition))]
    [MapperIgnoreTarget(nameof(Challenge.Mode))]
    public static void ApplyContentAsTemplateManager(
        ChallengeTemplateContentPatchRequest request,
        [MappingTarget] Challenge target)
    {
        target.Visibility = ToDomain(request.Visibility);
        target.Title = request.Title;
        target.Description = request.Description;
        target.Direction = request.Direction;
    }

    [MapperIgnoreTarget(nameof(Challenge.Mode))]
    [MapperIgnoreTarget(nameof(Challenge.Visibility))]
    [MapperIgnoreTarget(nameof(Challenge.Title))]
    [MapperIgnoreTarget(nameof(Challenge.Description))]
    [MapperIgnoreTarget(nameof(Challenge.Direction))]
    [MapperIgnoreTarget(nameof(Challenge.Definition))]
    [MapperIgnoreTarget(nameof(Challenge.Id))]
    [MapperIgnoreTarget(nameof(Challenge.Managers))]
    [MapperIgnoreTarget(nameof(Challenge.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(Challenge.CreatedAt))]
    [MapperIgnoreTarget(nameof(Challenge.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Challenge.DeletedAt))]
    [MapperIgnoreTarget(nameof(Challenge.Attachments))]
    [MapperIgnoreTarget(nameof(Challenge.NormalizedTitle))]
    [MapperIgnoreTarget(nameof(Challenge.NormalizedDirection))]
    public static partial void ApplyPermissionsAsTemplateOwner(
        ChallengeTemplatePermissionsPatchRequest request,
        [MappingTarget] Challenge target);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ChallengeVisibility ToDomain(ChallengeVisibilityProtocol value);
}

public sealed class PatchChallengeTemplateEndpoint(
    GetChallengeTemplate get,
    UpdateChallengeTemplate update,
    UpdateChallengeTemplatePermissions updatePermissions,
    TransferChallengeTemplateOwner transferOwner,
    IAtomicAggregatePatch atomicPatch,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<PatchChallengeTemplateRequest,
        Results<Ok<ChallengeTemplateResponse>, NotFound,
            ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankPatchTemplate"));
        Summary(summary => summary.Summary = "Updates selected challenge-template aggregate sections.");
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound,
        ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var challengeId = Route<Guid>("challengeId");
        var sections = ResolveSections(request);
        var current = await get.ExecuteAsync(
            challengeId,
            user.UserId,
            user.IsAdministrator,
            includeDeleted: false,
            ct);
        if (current is null)
            return TypedResults.NotFound();
        if ((sections & ChallengeTemplatePatchSection.Permissions) != 0
            && !user.IsAdministrator
            && current.OwnerId != user.UserId)
            return TypedResults.Forbid();

        var target = ChallengeGeneratedCatalog.Create(current.Mode);
        target.Id = current.Id;
        target.OwnerId = current.OwnerId;
        target.ManagerIds = current.ManagerIds.ToArray();
        target.Visibility = current.Visibility;
        target.Title = current.Title;
        target.Description = current.Description;
        target.Direction = current.Direction;
        target.Definition = current.Definition;
        target.CreatedAt = current.CreatedAt;
        target.UpdatedAt = current.UpdatedAt;
        target.DeletedAt = current.DeletedAt;
        if ((sections & ChallengeTemplatePatchSection.Content) != 0)
        {
            ChallengeTemplatePatchMapper.ApplyContentAsTemplateManager(request.Content!, target);
            target.Definition = ChallengeDefinitionContractMapper.ToDomain(
                challengeId,
                target.Mode,
                request.Content!.Definition);
        }
        if ((sections & ChallengeTemplatePatchSection.Permissions) != 0)
            ChallengeTemplatePatchMapper.ApplyPermissionsAsTemplateOwner(
                request.Permissions!, target);

        if ((sections & ChallengeTemplatePatchSection.Permissions) != 0
            && target.OwnerId != current.OwnerId
            && !target.ManagerIds.Contains(current.OwnerId))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Challenge template permissions are invalid.",
                detail: "The previous owner must remain in ManagerIds when ownership changes.");
        }

        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<ChallengeTemplateResponse>,
            NotFound, ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>>
            ApplyAsync(CancellationToken transactionCt)
        {
            ChallengeTemplateWriteResult result = new(
                ChallengeTemplateWriteState.Succeeded,
                current);
            if ((sections & ChallengeTemplatePatchSection.Permissions) != 0
                && target.OwnerId != current.OwnerId)
            {
                result = await transferOwner.ExecuteAsync(
                    challengeId,
                    user.UserId,
                    user.IsAdministrator,
                    target.OwnerId,
                    timeProvider.GetUtcNow(),
                    transactionCt);
                if (!result.Succeeded)
                    return AtomicAggregatePatchDecision<Results<Ok<ChallengeTemplateResponse>,
                        NotFound, ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
                        ProblemHttpResult>>
                        .Rollback(MapFailure(result));
            }
            if ((sections & ChallengeTemplatePatchSection.Permissions) != 0)
            {
                result = await updatePermissions.ExecuteAsync(
                    challengeId,
                    user.UserId,
                    user.IsAdministrator,
                    target.ManagerIds,
                    timeProvider.GetUtcNow(),
                    transactionCt);
                if (!result.Succeeded)
                    return AtomicAggregatePatchDecision<Results<Ok<ChallengeTemplateResponse>,
                        NotFound, ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
                        ProblemHttpResult>>
                        .Rollback(MapFailure(result));
            }
            if ((sections & ChallengeTemplatePatchSection.Content) != 0)
            {
                result = await update.ExecuteAsync(new UpdateChallengeTemplateCommand(
                    challengeId,
                    user.UserId,
                    user.IsAdministrator,
                    target.Mode,
                    target.Visibility,
                    target.Title,
                    target.Description,
                    target.Direction,
                    target.Definition!,
                    timeProvider.GetUtcNow()), transactionCt);
                if (!result.Succeeded)
                    return AtomicAggregatePatchDecision<Results<Ok<ChallengeTemplateResponse>,
                        NotFound, ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
                        ProblemHttpResult>>
                        .Rollback(MapFailure(result));
            }

            var refreshed = await get.ExecuteAsync(
                challengeId,
                user.UserId,
                user.IsAdministrator,
                includeDeleted: false,
                transactionCt);
            Results<Ok<ChallengeTemplateResponse>, NotFound,
                ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
                ProblemHttpResult> response =
                refreshed is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ChallengeTemplateMapper.ToResponse(refreshed));
            return AtomicAggregatePatchDecision<Results<Ok<ChallengeTemplateResponse>,
                NotFound, ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>,
                ProblemHttpResult>>
                .Commit(response);
        }
    }

    private static Results<Ok<ChallengeTemplateResponse>, NotFound,
        ForbidHttpResult, Conflict<ChallengeTemplateConflictResponse>, ProblemHttpResult> MapFailure(
        ChallengeTemplateWriteResult result) =>
        result.State switch
        {
            ChallengeTemplateWriteState.NotFoundOrForbidden => TypedResults.NotFound(),
            ChallengeTemplateWriteState.ActiveCompetitionModeConflict
                or ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict
                or ChallengeTemplateWriteState.OwnerIncludedInManagerSet
                or ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible
                or ChallengeTemplateWriteState.ExperimentalFeatureDisabled
                or ChallengeTemplateWriteState.InteractionKindConflict =>
                TypedResults.Conflict(ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            ChallengeTemplateWriteState.InvalidRequest
                or ChallengeTemplateWriteState.InvalidDefinition =>
                TypedResults.Problem(ApiValidationProblemFactory.Create(
                    [
                        new ValidationFailure(
                            result.State == ChallengeTemplateWriteState.InvalidDefinition
                                ? "Content.Definition"
                                : "Request",
                            result.Detail ?? "Challenge template update is invalid.")
                    ],
                    StatusCodes.Status400BadRequest)),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Challenge template was not updated.",
                detail: result.Detail)
        };

    private static ChallengeTemplatePatchSection ResolveSections(
        PatchChallengeTemplateRequest request) =>
        (request.Content is null ? ChallengeTemplatePatchSection.None
            : ChallengeTemplatePatchSection.Content)
        | (request.Permissions is null ? ChallengeTemplatePatchSection.None
            : ChallengeTemplatePatchSection.Permissions);
}
