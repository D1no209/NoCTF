using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class CreatePlatformBotRequest
{
    private string userName = string.Empty;

    public string UserName
    {
        get => userName;
        set => userName = value?.Trim() ?? string.Empty;
    }
    public UserRoleProtocol Role { get; set; } = UserRoleProtocol.Organizer;
}

public sealed class CreatePlatformBotValidator : Validator<CreatePlatformBotRequest>
{
    public CreatePlatformBotValidator()
    {
        RuleFor(request => request.UserName)
            .NotEmpty()
            .Length(3, 64)
            .Matches("^[A-Za-z0-9_-]+$");
        RuleFor(request => request.Role).IsInEnum();
    }
}

public sealed class CreatePlatformBotEndpoint(ManagePlatform platform, TimeProvider timeProvider)
    : Endpoint<CreatePlatformBotRequest,
        Results<Created<PlatformUserResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/bots");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformCreateBot"));
        Summary(summary =>
        {
            summary.Summary = "Creates a non-interactive integration Bot.";
            summary.Description =
                "Creates a role-bearing Bot that uses ordinary Access JWT authorization without password login or refresh tokens.";
        });
    }

    public override async Task<
        Results<Created<PlatformUserResponse>, ProblemHttpResult>> ExecuteAsync(
        CreatePlatformBotRequest request,
        CancellationToken ct)
    {
        var result = await platform.CreateBotAsync(
            request.UserName,
            IdentityProtocolMapper.ToDomain(request.Role),
            timeProvider.GetUtcNow(),
            ct);
        if (result.State == CreateBotState.UserNameConflict)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ApiMessages.Get(ApiMessageId.CreatePlatformBotTitleBotWasCreated),
                detail: ApiMessages.Get(ApiMessageId.CreatePlatformBotDetailRequestedUserNameAlready));
        if (result.State is CreateBotState.InvalidUserName or CreateBotState.InvalidRole)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.CreatePlatformBotTitleBotWasCreated),
                detail: result.State == CreateBotState.InvalidRole
                    ? ApiMessages.Get(ApiMessageId.BotRoleInvalid)
                    : ApiMessages.Get(ApiMessageId.BotUserNameInvalid));

        var response = PlatformUserMapping.ToResponse(result.User!);
        return TypedResults.Created($"/api/v1/admin/platform/users/{response.Id}", response);
    }
}
