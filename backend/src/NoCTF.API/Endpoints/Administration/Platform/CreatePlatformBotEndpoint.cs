using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.Bots;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class CreatePlatformBotRequest
{
    public string UserName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Organizer;
}

public sealed class CreatePlatformBotValidator : Validator<CreatePlatformBotRequest>
{
    public CreatePlatformBotValidator()
    {
        RuleFor(request => request.UserName).NotEmpty().MaximumLength(64);
        RuleFor(request => request.Role).Equal(UserRole.Organizer);
    }
}

public sealed class CreatePlatformBotEndpoint(CreatePlatformBot create)
    : Endpoint<CreatePlatformBotRequest,
        Results<Created<PlatformUserResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/bots");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder
            .WithName("AdminPlatformCreateBot")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Creates a non-interactive Organizer Bot.";
            summary.Description =
                "Creates a Bot identity with no password-login or refresh-token capability.";
        });
    }

    public override async Task<
        Results<Created<PlatformUserResponse>, ProblemHttpResult>>
        ExecuteAsync(CreatePlatformBotRequest request, CancellationToken ct)
    {
        var result = await create.ExecuteAsync(
            request.UserName,
            request.Role,
            DateTimeOffset.UtcNow,
            ct);
        if (result.Succeeded)
        {
            var response = PlatformUserMapping.ToResponse(result.Value!);
            return TypedResults.Created(
                $"/api/v1/admin/platform/users/{response.Id}",
                response);
        }
        if (result.ErrorCode == "username_conflict")
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Bot UserName is already in use.",
                detail: result.ErrorMessage);
        }
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bot was not created.",
            detail: result.ErrorMessage);
    }
}
