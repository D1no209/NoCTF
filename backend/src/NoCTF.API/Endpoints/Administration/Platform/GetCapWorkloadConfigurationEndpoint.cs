using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CapWorkloadProblemCode>))]
public enum CapWorkloadProblemCode
{
    CapWorkloadProviderNotCap,
    CapWorkloadDifficultyInvalid,
    CapWorkloadChallengeCountInvalid,
    CapWorkloadManagementCredentialMissing,
    CapWorkloadManagementCredentialInvalid,
    CapWorkloadSiteKeyNotFound,
    CapWorkloadProviderUnavailable,
    CapWorkloadConfigurationNotApplied
}

public sealed record CapWorkloadConfigurationResponse(
    int Difficulty,
    int ChallengeCount,
    int ChallengeSize,
    long ExpectedHashAttempts);

internal static class CapWorkloadConfigurationHttpMapping
{
    public static CapWorkloadConfigurationResponse ToResponse(
        CapWorkloadConfiguration configuration) => new(
        configuration.Difficulty,
        configuration.ChallengeCount,
        configuration.ChallengeSize,
        configuration.ExpectedHashAttempts);

    public static ProblemHttpResult ToProblem(
        CapWorkloadConfigurationError error) => TypedResults.Problem(
        statusCode: Status(error),
        title: "CAP workload configuration is unavailable.",
        detail: Detail(error),
        extensions: new Dictionary<string, object?>
        {
            ["code"] = ToProtocol(error)
        });

    private static int Status(CapWorkloadConfigurationError error) => error switch
    {
        CapWorkloadConfigurationError.DifficultyInvalid
            or CapWorkloadConfigurationError.ChallengeCountInvalid =>
            StatusCodes.Status400BadRequest,
        CapWorkloadConfigurationError.ProviderNotCap
            or CapWorkloadConfigurationError.SiteKeyNotFound
            or CapWorkloadConfigurationError.ConfigurationNotApplied =>
            StatusCodes.Status409Conflict,
        CapWorkloadConfigurationError.ManagementCredentialInvalid =>
            StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string Detail(CapWorkloadConfigurationError error) => error switch
    {
        CapWorkloadConfigurationError.ProviderNotCap =>
            "CAP must be the active human verification provider.",
        CapWorkloadConfigurationError.DifficultyInvalid =>
            "Difficulty must be between 1 and 8.",
        CapWorkloadConfigurationError.ChallengeCountInvalid =>
            "Challenge count must be between 1 and 500.",
        CapWorkloadConfigurationError.ManagementCredentialMissing =>
            "The deployment has no CAP management API key.",
        CapWorkloadConfigurationError.ManagementCredentialInvalid =>
            "CAP rejected the configured management API key.",
        CapWorkloadConfigurationError.SiteKeyNotFound =>
            "CAP could not find the configured site key.",
        CapWorkloadConfigurationError.ConfigurationNotApplied =>
            "CAP did not retain the requested workload configuration.",
        _ => "CAP did not return a usable workload configuration."
    };

    private static CapWorkloadProblemCode ToProtocol(
        CapWorkloadConfigurationError error) => error switch
        {
            CapWorkloadConfigurationError.ProviderNotCap =>
                CapWorkloadProblemCode.CapWorkloadProviderNotCap,
            CapWorkloadConfigurationError.DifficultyInvalid =>
                CapWorkloadProblemCode.CapWorkloadDifficultyInvalid,
            CapWorkloadConfigurationError.ChallengeCountInvalid =>
                CapWorkloadProblemCode.CapWorkloadChallengeCountInvalid,
            CapWorkloadConfigurationError.ManagementCredentialMissing =>
                CapWorkloadProblemCode.CapWorkloadManagementCredentialMissing,
            CapWorkloadConfigurationError.ManagementCredentialInvalid =>
                CapWorkloadProblemCode.CapWorkloadManagementCredentialInvalid,
            CapWorkloadConfigurationError.SiteKeyNotFound =>
                CapWorkloadProblemCode.CapWorkloadSiteKeyNotFound,
            CapWorkloadConfigurationError.ProviderUnavailable =>
                CapWorkloadProblemCode.CapWorkloadProviderUnavailable,
            CapWorkloadConfigurationError.ConfigurationNotApplied =>
                CapWorkloadProblemCode.CapWorkloadConfigurationNotApplied,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null)
        };
}

public sealed class GetCapWorkloadConfigurationEndpoint(
    ManageCapWorkloadConfiguration workload)
    : EndpointWithoutRequest<
        Results<Ok<CapWorkloadConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/platform/human-verification/cap-workload");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName(
            "AdminPlatformGetCapWorkloadConfiguration"));
        Summary(summary => summary.Summary =
            "Returns the active CAP site's proof-of-work configuration.");
    }

    public override async Task<
        Results<Ok<CapWorkloadConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await workload.GetAsync(ct);
        return result.Configuration is not null
            ? TypedResults.Ok(CapWorkloadConfigurationHttpMapping.ToResponse(
                result.Configuration))
            : CapWorkloadConfigurationHttpMapping.ToProblem(result.Error!.Value);
    }
}
