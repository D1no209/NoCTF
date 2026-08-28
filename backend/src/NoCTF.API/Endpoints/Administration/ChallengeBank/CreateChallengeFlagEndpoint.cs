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
    public string Flag { get; set; } = string.Empty;
    public ChallengeFlagMatchKindProtocol MatchKind { get; set; }
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
            scope, flagId, isCreate, null, request.Flag,
            null, null, null, null, now,
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

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeFlagFailureCodeProtocol>))]
public enum ChallengeFlagFailureCodeProtocol
{
    InvalidFlag,
    InvalidRegularExpression,
    RegularExpressionNotSupported,
    InvalidSpecification,
    InvalidValidityWindow,
    InvalidTemplateFlagScope,
    ResourceIdConflict,
    ManualFlagNotSupported,
    SystemManagedFlag,
    DeliveryModeConflict
}

public sealed record ChallengeFlagFailureResponse(
    ChallengeFlagFailureCodeProtocol Code,
    string Message);

internal static class ChallengeFlagFailureMapping
{
    public static ChallengeFlagFailureResponse ToResponse(
        ChallengeFlagFailureCode code,
        string? message) =>
        new(
            code switch
            {
                ChallengeFlagFailureCode.InvalidFlag => ChallengeFlagFailureCodeProtocol.InvalidFlag,
                ChallengeFlagFailureCode.InvalidRegularExpression => ChallengeFlagFailureCodeProtocol.InvalidRegularExpression,
                ChallengeFlagFailureCode.RegularExpressionNotSupported => ChallengeFlagFailureCodeProtocol.RegularExpressionNotSupported,
                ChallengeFlagFailureCode.InvalidSpecification => ChallengeFlagFailureCodeProtocol.InvalidSpecification,
                ChallengeFlagFailureCode.InvalidValidityWindow => ChallengeFlagFailureCodeProtocol.InvalidValidityWindow,
                ChallengeFlagFailureCode.InvalidTemplateFlagScope => ChallengeFlagFailureCodeProtocol.InvalidTemplateFlagScope,
                ChallengeFlagFailureCode.ResourceIdConflict => ChallengeFlagFailureCodeProtocol.ResourceIdConflict,
                ChallengeFlagFailureCode.ManualFlagNotSupported => ChallengeFlagFailureCodeProtocol.ManualFlagNotSupported,
                ChallengeFlagFailureCode.SystemManagedFlag => ChallengeFlagFailureCodeProtocol.SystemManagedFlag,
                ChallengeFlagFailureCode.DeliveryModeConflict => ChallengeFlagFailureCodeProtocol.DeliveryModeConflict,
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
            },
            message ?? "Flag operation failed.");
}

public sealed class CreateChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<SaveChallengeFlagRequest, Results<
        Created<ChallengeFlagResponse>,
        NotFound,
        Conflict<ChallengeFlagFailureResponse>>>
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

    public override async Task<Results<
        Created<ChallengeFlagResponse>,
        NotFound,
        Conflict<ChallengeFlagFailureResponse>>> ExecuteAsync(
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
                timeProvider.GetUtcNow()),
            user.UserId,
            user.IsAdministrator,
            ct);
        if (result.FailureCode == ChallengeFlagFailureCode.FlagNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var failure = ChallengeFlagFailureMapping.ToResponse(result.FailureCode!.Value, result.ErrorMessage);
            return TypedResults.Conflict(failure);
        }
        var response = ChallengeFlagMapping.ToResponse(result.Value!);
        return TypedResults.Created($"/api/v1/admin/challenges/{challengeId}/flags/{response.Id}", response);
    }
}
