using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdateCapWorkloadConfigurationRequest
{
    public required int Difficulty { get; set; }
    public required int ChallengeCount { get; set; }
}

public sealed class UpdateCapWorkloadConfigurationValidator
    : Validator<UpdateCapWorkloadConfigurationRequest>
{
    public UpdateCapWorkloadConfigurationValidator()
    {
        RuleFor(request => request.Difficulty).InclusiveBetween(
            CapWorkloadConfigurationRules.MinimumDifficulty,
            CapWorkloadConfigurationRules.MaximumDifficulty);
        RuleFor(request => request.ChallengeCount).InclusiveBetween(
            CapWorkloadConfigurationRules.MinimumChallengeCount,
            CapWorkloadConfigurationRules.MaximumChallengeCount);
    }
}

public sealed class UpdateCapWorkloadConfigurationEndpoint(
    ManageCapWorkloadConfiguration workload)
    : Endpoint<UpdateCapWorkloadConfigurationRequest,
        Results<Ok<CapWorkloadConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/human-verification/cap-workload");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName(
            "AdminPlatformUpdateCapWorkloadConfiguration"));
        Summary(summary => summary.Summary =
            "Updates and confirms the active CAP site's proof-of-work configuration.");
    }

    public override async Task<
        Results<Ok<CapWorkloadConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UpdateCapWorkloadConfigurationRequest request,
        CancellationToken ct)
    {
        var result = await workload.UpdateAsync(
            request.Difficulty,
            request.ChallengeCount,
            ct);
        return result.Configuration is not null
            ? TypedResults.Ok(CapWorkloadConfigurationHttpMapping.ToResponse(
                result.Configuration))
            : CapWorkloadConfigurationHttpMapping.ToProblem(result.Error!.Value);
    }
}
