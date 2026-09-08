using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.PublicAccess;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdatePublicGatewayRequest
{
    public bool Enabled { get; set; }
    public string ConnectorId { get; set; } = string.Empty;
    public string PublicOrigin { get; set; } = string.Empty;
    public string[] DirectOrigins { get; set; } = [];
    public string PublicRuntimeHost { get; set; } = string.Empty;
    public string? DirectRuntimeHostOverride { get; set; }
    public int MaxPublishedPorts { get; set; }
}
public sealed class UpdatePublicGatewayValidator : Validator<UpdatePublicGatewayRequest>
{
    public UpdatePublicGatewayValidator()
    {
        RuleFor(x => x.ConnectorId).NotNull().MaximumLength(128);
        RuleFor(x => x.PublicOrigin).NotNull().MaximumLength(2048);
        RuleFor(x => x.DirectOrigins).Cascade(CascadeMode.Stop).NotNull().Must(value => value.Length <= 16);
        RuleForEach(x => x.DirectOrigins).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.PublicRuntimeHost).NotNull().MaximumLength(253);
        RuleFor(x => x.DirectRuntimeHostOverride).MaximumLength(253);
        RuleFor(x => x.MaxPublishedPorts).InclusiveBetween(0, 64);
    }
}
public sealed record PublicGatewayAcceptedResponse(PublicGatewayConfigurationResponse Configuration, string StatusUrl);
public sealed class UpdatePublicGatewayEndpoint(ManagePublicGateway gateway, TimeProvider clock)
    : Endpoint<UpdatePublicGatewayRequest, Results<Accepted<PublicGatewayAcceptedResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/public-gateway");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUpdatePublicGateway"));
        Summary(summary =>
        {
            summary.Summary = "Saves optional public gateway configuration.";
            summary.Description = "Validates deployment capability boundaries and saves policy for asynchronous application. Saving does not imply public connectivity is ready.";
        });
    }
    public override async Task<Results<Accepted<PublicGatewayAcceptedResponse>, ProblemHttpResult>> ExecuteAsync(UpdatePublicGatewayRequest request, CancellationToken ct)
    {
        var result = await gateway.SaveAsync(new(request.Enabled, request.ConnectorId, request.PublicOrigin,
            request.DirectOrigins, request.PublicRuntimeHost, request.DirectRuntimeHostOverride, request.MaxPublishedPorts), clock.GetUtcNow(), ct);
        if (result.Errors.Count > 0)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Public gateway configuration is invalid.", detail: string.Join(" ", result.Errors));
        const string statusUrl = "/api/v1/admin/platform/public-gateway/status";
        return TypedResults.Accepted(statusUrl, new PublicGatewayAcceptedResponse(PublicGatewayConfigurationMapping.ToResponse(result.Configuration!), statusUrl));
    }
}
