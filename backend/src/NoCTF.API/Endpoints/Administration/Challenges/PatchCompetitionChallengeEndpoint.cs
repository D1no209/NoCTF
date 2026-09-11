using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Common;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using Riok.Mapperly.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Challenges;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionChallengeConflictCode>))]
public enum CompetitionChallengeConflictCode
{
    ResourceIdConflict,
    ChallengeOrderConflict,
    ChallengeTemplateConflict,
    LifecycleStateConflict,
    ChallengeTemplateNotFound,
    ChallengeTemplateModeMismatch
}

public sealed record CompetitionChallengeConflictResponse(
    [property: Required, JsonRequired] CompetitionChallengeConflictCode Code,
    [property: Required, JsonRequired] string Detail);

internal static class CompetitionChallengeConflictMapper
{
    public static CompetitionChallengeConflictResponse ToResponse(
        ChallengeMutationFailure failure) =>
        new(
            failure switch
            {
                ChallengeMutationFailure.ResourceIdConflict => CompetitionChallengeConflictCode.ResourceIdConflict,
                ChallengeMutationFailure.ChallengeOrderConflict => CompetitionChallengeConflictCode.ChallengeOrderConflict,
                ChallengeMutationFailure.ChallengeTemplateConflict => CompetitionChallengeConflictCode.ChallengeTemplateConflict,
                ChallengeMutationFailure.LifecycleStateConflict => CompetitionChallengeConflictCode.LifecycleStateConflict,
                ChallengeMutationFailure.TemplateNotFound => CompetitionChallengeConflictCode.ChallengeTemplateNotFound,
                ChallengeMutationFailure.TemplateModeMismatch => CompetitionChallengeConflictCode.ChallengeTemplateModeMismatch,
                _ => throw new InvalidOperationException($"Unsupported competition challenge conflict: {failure}.")
            },
            failure.ToString());
}

public sealed class CompetitionChallengePresentationPatchRequest
{
    public required string? CustomTitle { get; set; }
    public required int Order { get; set; }
    public required bool IsPublished { get; set; }
}

public sealed class CompetitionChallengeRulesPatchRequest
{
    public required string Json { get; set; }
}

public sealed class PatchCompetitionChallengeRequest
{
    public CompetitionChallengePresentationPatchRequest? Presentation { get; set; }
    public CompetitionChallengeRulesPatchRequest? Rules { get; set; }
}

[Flags]
internal enum CompetitionChallengePatchSection
{
    None = 0,
    Presentation = 1 << 0,
    Rules = 1 << 1
}

public sealed class PatchCompetitionChallengeValidator
    : Validator<PatchCompetitionChallengeRequest>
{
    public PatchCompetitionChallengeValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Presentation is not null || request.Rules is not null)
            .WithMessage("At least one competition-challenge section is required.");
        RuleFor(request => request.Presentation!.CustomTitle).MaximumLength(160)
            .When(request => request.Presentation is not null);
        RuleFor(request => request.Presentation!.Order).GreaterThanOrEqualTo(0)
            .When(request => request.Presentation is not null);
        RuleFor(request => request.Rules!.Json).NotEmpty()
            .When(request => request.Rules is not null);
    }
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both)]
public static partial class CompetitionChallengePatchMapper
{
    [MapperIgnoreTarget(nameof(CompetitionChallenge.Id))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.CompetitionId))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.ChallengeId))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.RulesJson))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.UpdatedAt))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.DeletedAt))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.Hints))]
    public static partial void ApplyPresentationAsCompetitionModerator(
        CompetitionChallengePresentationPatchRequest request,
        [MappingTarget] CompetitionChallenge target);

    [MapProperty(nameof(CompetitionChallengeRulesPatchRequest.Json), nameof(CompetitionChallenge.RulesJson))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.Id))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.CompetitionId))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.ChallengeId))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.CustomTitle))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.Order))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.IsPublished))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.UpdatedAt))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.DeletedAt))]
    [MapperIgnoreTarget(nameof(CompetitionChallenge.Hints))]
    public static partial void ApplyRulesAsCompetitionModerator(
        CompetitionChallengeRulesPatchRequest request,
        [MappingTarget] CompetitionChallenge target);
}

public sealed class PatchCompetitionChallengeEndpoint(
    GetChallenge get,
    GetChallengeConfiguration getConfiguration,
    UpdateChallenge update,
    UpdateChallengeConfiguration updateConfiguration,
    IAtomicAggregatePatch atomicPatch,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<PatchCompetitionChallengeRequest,
        Results<Ok<AdminCompetitionChallengeResponse>, NotFound,
            ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPatchCompetitionChallenge"));
        Summary(summary => summary.Summary = "Updates selected competition-challenge sections.");
    }

    public override async Task<Results<Ok<AdminCompetitionChallengeResponse>, NotFound,
        ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchCompetitionChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var sections = ResolveSections(request);
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var current = await get.ExecuteAsync(
            competitionId,
            competitionChallengeId,
            includeUnpublished: true,
            includeDeleted: false,
            ct);
        var configuration = await getConfiguration.ExecuteAsync(
            competitionId,
            competitionChallengeId,
            ct);
        if (current is null || configuration is null)
            return TypedResults.NotFound();

        var target = new CompetitionChallenge
        {
            Id = current.Id,
            CompetitionId = current.CompetitionId,
            ChallengeId = current.ChallengeId,
            CustomTitle = current.CustomTitle,
            Order = current.Order,
            IsPublished = current.IsPublished,
            RulesJson = configuration.Json,
            UpdatedAt = current.UpdatedAt,
            DeletedAt = current.DeletedAt
        };
        if ((sections & CompetitionChallengePatchSection.Presentation) != 0)
            CompetitionChallengePatchMapper.ApplyPresentationAsCompetitionModerator(
                request.Presentation!, target);
        if ((sections & CompetitionChallengePatchSection.Rules) != 0)
            CompetitionChallengePatchMapper.ApplyRulesAsCompetitionModerator(request.Rules!, target);

        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionChallengeResponse>,
            NotFound, ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>>> ApplyAsync(CancellationToken transactionCt)
        {
            if ((sections & CompetitionChallengePatchSection.Presentation) != 0)
            {
                var result = await update.ExecuteAsync(new UpdateCompetitionChallengeCommand(
                    competitionId,
                    competitionChallengeId,
                    target.Order,
                    target.IsPublished,
                    timeProvider.GetUtcNow(),
                    target.CustomTitle), transactionCt);
                if (result.Challenge is null)
                {
                    Results<Ok<AdminCompetitionChallengeResponse>, NotFound,
                        ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                        ProblemHttpResult> failure = result.Failure switch
                        {
                            ChallengeMutationFailure.CompetitionNotFound
                                or ChallengeMutationFailure.ChallengeNotFound =>
                                TypedResults.NotFound(),
                            ChallengeMutationFailure.ChallengeOrderConflict =>
                                TypedResults.Conflict(CompetitionChallengeConflictMapper.ToResponse(
                                    result.Failure.Value)),
                            _ => TypedResults.Problem(
                                statusCode: StatusCodes.Status400BadRequest,
                                title: "Competition challenge was not updated.",
                                detail: result.Failure?.ToString())
                        };
                    return AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionChallengeResponse>,
                        NotFound, ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                        ProblemHttpResult>>.Rollback(failure);
                }
            }
            if ((sections & CompetitionChallengePatchSection.Rules) != 0)
            {
                var result = await updateConfiguration.ExecuteAsync(
                    competitionId,
                    competitionChallengeId,
                    target.RulesJson,
                    timeProvider.GetUtcNow(),
                    transactionCt);
                if (!result.Succeeded)
                {
                    Results<Ok<AdminCompetitionChallengeResponse>, NotFound,
                        ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                        ProblemHttpResult> failure = result.FailureCode switch
                    {
                        ChallengeConfigurationFailureCode.CompetitionNotFound
                            or ChallengeConfigurationFailureCode.ChallengeNotFound =>
                            TypedResults.NotFound(),
                        ChallengeConfigurationFailureCode.ConfigurationLocked =>
                            TypedResults.Conflict(new CompetitionChallengeConflictResponse(
                                CompetitionChallengeConflictCode.LifecycleStateConflict,
                                result.ErrorMessage
                                    ?? "Challenge rules are locked by the competition lifecycle.")),
                        _ => TypedResults.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Challenge rules are invalid.",
                            detail: result.ErrorMessage,
                            extensions: new Dictionary<string, object?>
                            {
                                ["code"] = result.FailureCode?.ToString()
                            })
                    };
                    return AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionChallengeResponse>,
                        NotFound, ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                        ProblemHttpResult>>.Rollback(failure);
                }
            }

            var refreshed = await get.ExecuteAsync(
                competitionId,
                competitionChallengeId,
                includeUnpublished: true,
                includeDeleted: false,
                transactionCt);
            var refreshedConfiguration = await getConfiguration.ExecuteAsync(
                competitionId,
                competitionChallengeId,
                transactionCt);
            Results<Ok<AdminCompetitionChallengeResponse>, NotFound,
                ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                ProblemHttpResult> response = refreshed is null || refreshedConfiguration is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(new AdminCompetitionChallengeResponse(
                        ChallengeMapper.ToResponse(refreshed),
                        CompetitionProtocolMapper.ToProtocol(refreshedConfiguration.Mode),
                        CompetitionProtocolMapper.ToProtocol(
                            refreshedConfiguration.CompetitionStatus),
                        refreshedConfiguration.Json));
            return AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionChallengeResponse>,
                NotFound, ForbidHttpResult, Conflict<CompetitionChallengeConflictResponse>,
                ProblemHttpResult>>.Commit(response);
        }
    }

    private static CompetitionChallengePatchSection ResolveSections(
        PatchCompetitionChallengeRequest request) =>
        (request.Presentation is null ? CompetitionChallengePatchSection.None
            : CompetitionChallengePatchSection.Presentation)
        | (request.Rules is null ? CompetitionChallengePatchSection.None
            : CompetitionChallengePatchSection.Rules);
}
