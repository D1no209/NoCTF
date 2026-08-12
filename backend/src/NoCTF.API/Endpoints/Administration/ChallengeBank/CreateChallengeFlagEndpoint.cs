using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class SaveChallengeFlagRequest
{
    public Guid? Id { get; set; }
    public Guid? TeamId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public ChallengeFlagMatchKindProtocol MatchKind { get; set; }
    public SpecificationKindProtocol? SpecificationKind { get; set; }
    public Guid? SpecificationId { get; set; }
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
}

public sealed class SaveChallengeFlagValidator : Validator<SaveChallengeFlagRequest>
{
    public SaveChallengeFlagValidator()
    {
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
        RuleFor(request => request.Flag).NotEmpty().MaximumLength(4096);
        RuleFor(request => request.MatchKind).IsInEnum();
        RuleFor(request => request.SpecificationKind)
            .IsInEnum()
            .When(request => request.SpecificationKind is not null);
    }
}

internal static class SaveChallengeFlagMapping
{
    public static SaveChallengeFlagCommand ToCommand(
        SaveChallengeFlagRequest request,
        ChallengeFlagScope scope,
        Guid? flagId,
        bool isCreate,
        DateTimeOffset now) =>
        new(
            scope, flagId, isCreate, request.TeamId, request.Flag,
            request.SpecificationKind is null
                ? null
                : ChallengeTemplateMapper.ToDomain(request.SpecificationKind.Value),
            request.SpecificationId,
            request.ValidStart, request.ValidUntil, now,
            request.MatchKind switch
            {
                ChallengeFlagMatchKindProtocol.Exact => ChallengeFlagMatchKind.Exact,
                ChallengeFlagMatchKindProtocol.RegularExpression => ChallengeFlagMatchKind.RegularExpression,
                _ => throw new ArgumentOutOfRangeException(nameof(request.MatchKind))
            });
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeFlagMatchKindProtocol>))]
public enum ChallengeFlagMatchKindProtocol
{
    Exact,
    RegularExpression
}

public sealed class CreateChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : Endpoint<SaveChallengeFlagRequest, Results<Created<ChallengeFlagResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/flags");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankCreateFlag"));
        Summary(summary =>
        {
            summary.Summary = "Creates a template-level static flag.";
            summary.Description = "Adds protected static flag material to a global challenge template.";
        });
    }

    public override async Task<Results<Created<ChallengeFlagResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        SaveChallengeFlagRequest request,
        CancellationToken ct)
    {
        var challengeId = Route<Guid>("challengeId");
        var result = await flags.SaveAsync(
            SaveChallengeFlagMapping.ToCommand(
                request,
                ChallengeFlagScope.Template(challengeId),
                request.Id,
                isCreate: true,
                DateTimeOffset.UtcNow),
            user.UserId,
            user.IsAdministrator,
            ct);
        if (result.FailureCode == ChallengeFlagFailureCode.FlagNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: result.FailureCode == ChallengeFlagFailureCode.ResourceIdConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Flag was not created.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.FailureCode?.ToString() });
        var response = ChallengeFlagMapping.ToResponse(result.Value!);
        return TypedResults.Created($"/api/v1/admin/challenges/{challengeId}/flags/{response.Id}", response);
    }
}
