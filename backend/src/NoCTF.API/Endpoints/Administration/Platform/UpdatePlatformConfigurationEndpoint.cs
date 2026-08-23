using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdatePlatformConfigurationRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}

public sealed class UpdatePlatformConfigurationValidator
    : Validator<UpdatePlatformConfigurationRequest>
{
    public UpdatePlatformConfigurationValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(PlatformConfigurationRules.MaximumNameLength);
        RuleFor(request => request.Description)
            .MaximumLength(PlatformConfigurationRules.MaximumDescriptionLength);
    }
}

public sealed class UpdatePlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links)
    : Endpoint<UpdatePlatformConfigurationRequest,
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUpdateConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "Updates platform name and description.";
            summary.Description = "Applies the public branding text.";
        });
    }

    public override async Task<
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UpdatePlatformConfigurationRequest request,
        CancellationToken ct)
    {
        var result = await configuration.UpdateAsync(
            request.Name,
            request.Description,
            DateTimeOffset.UtcNow,
            ct);
        return result.State switch
        {
            PlatformConfigurationUpdateState.Updated => TypedResults.Ok(
                PlatformConfigurationMapping.ToResponse(
                    result.Configuration!,
                    links,
                    HttpContext)),
            PlatformConfigurationUpdateState.InvalidInput => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Platform configuration is invalid."),
            _ => throw new InvalidOperationException(
                $"Unsupported platform configuration update state: {result.State}.")
        };
    }
}
