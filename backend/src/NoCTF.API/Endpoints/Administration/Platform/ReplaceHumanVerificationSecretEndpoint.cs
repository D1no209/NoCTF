using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ReplaceHumanVerificationSecretRequest
{
    public required HumanVerificationProviderProtocol Provider { get; set; }
    public required string Secret { get; set; }
}

public sealed class ReplaceHumanVerificationSecretValidator
    : Validator<ReplaceHumanVerificationSecretRequest>
{
    public ReplaceHumanVerificationSecretValidator()
    {
        RuleFor(request => request.Provider).IsInEnum()
            .NotEqual(HumanVerificationProviderProtocol.None);
        RuleFor(request => request.Secret).NotEmpty()
            .MaximumLength(HumanVerificationConfigurationRules.MaximumSecretLength);
    }
}

public sealed class ReplaceHumanVerificationSecretEndpoint(
    ManageHumanVerificationConfiguration configuration,
    TimeProvider timeProvider)
    : Endpoint<ReplaceHumanVerificationSecretRequest,
        Results<Ok<AdminHumanVerificationConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/human-verification/secret");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName(
            "AdminPlatformReplaceHumanVerificationSecret"));
        Summary(summary =>
        {
            summary.Summary = "Replaces a human verification provider secret.";
            summary.Description =
                "Encrypts the selected provider secret without returning it.";
        });
    }

    public override async Task<Results<Ok<AdminHumanVerificationConfigurationResponse>,
        ProblemHttpResult>> ExecuteAsync(
        ReplaceHumanVerificationSecretRequest request,
        CancellationToken ct)
    {
        var result = await configuration.ReplaceSecretAsync(
            PublicPlatformConfigurationMapping.ToDomain(request.Provider),
            request.Secret,
            timeProvider.GetUtcNow(),
            ct);
        if (result.State != HumanVerificationConfigurationUpdateState.Updated)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Human verification secret is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PlatformProblemCode.HumanVerificationSecretInvalid
                });
        }

        return TypedResults.Ok(AdminHumanVerificationConfigurationMapping.ToResponse(
            result.Configuration!));
    }
}
