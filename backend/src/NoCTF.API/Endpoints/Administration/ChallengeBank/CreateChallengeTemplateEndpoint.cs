using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class CreateChallengeTemplateRequest
{
    public ChallengeVisibility Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
}

public sealed record ChallengeTemplateResponse(
    Guid Id,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    ChallengeVisibility Visibility,
    string Title,
    string? Description,
    string Direction,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateChallengeTemplateValidator : Validator<CreateChallengeTemplateRequest>
{
    public CreateChallengeTemplateValidator()
    {
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
    }
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both)]
internal static partial class ChallengeTemplateMapper
{
    public static partial CreateChallengeTemplateCommand ToCommand(
        CreateChallengeTemplateRequest request,
        Guid ownerId,
        DateTimeOffset createdAt);
    public static partial ChallengeTemplateResponse ToResponse(ChallengeTemplateView source);
    private static partial IReadOnlyList<ChallengeTemplateResponse> ToResponses(
        IReadOnlyList<ChallengeTemplateView> source);
    public static ChallengeTemplateListResponse ToListResponse(IReadOnlyList<ChallengeTemplateView> source) =>
        new(ToResponses(source));
}

public sealed class CreateChallengeTemplateEndpoint(
    CreateChallengeTemplate create,
    IUserContext user)
    : Endpoint<CreateChallengeTemplateRequest, Results<Created<ChallengeTemplateResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Summary(summary =>
        {
            summary.Summary = "Creates a global challenge template.";
            summary.Description = "Creates a reusable question-bank template independent of any competition.";
        });
    }

    public override async Task<Results<Created<ChallengeTemplateResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var result = await create.ExecuteAsync(
            ChallengeTemplateMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow),
            ct);
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Challenge template was not created.",
                detail: result.ErrorMessage);
        var response = ChallengeTemplateMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/api/v1/admin/challenges/{response.Id}", response);
    }
}
