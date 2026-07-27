using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameMode Mode { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
}

public sealed class CreateCompetitionValidator : Validator<CreateCompetitionRequest>
{
    public CreateCompetitionValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.EndTime).GreaterThan(request => request.StartTime);
        RuleFor(request => request.MaxTeamMembers).GreaterThan(0);
    }
}

public sealed class CreateCompetitionEndpoint(CreateCompetition create, IUserContext user)
    : Endpoint<CreateCompetitionRequest, Results<Created<CompetitionResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminCreateCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Creates a competition.";
            summary.Description = "Creates a draft competition owned by the current organizer or administrator.";
        });
    }

    public override async Task<Results<Created<CompetitionResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateCompetitionRequest request,
        CancellationToken ct)
    {
        var result = await create.ExecuteAsync(new CreateCompetitionCommand(
            request.Title,
            request.Description,
            request.Mode,
            request.StartTime,
            request.EndTime,
            request.TeamRegistrationAutoApprove,
            request.MaxTeamMembers,
            user.UserId,
            DateTimeOffset.UtcNow), ct);
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Competition was not created.",
                detail: result.ErrorMessage);
        }

        var response = CompetitionMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/api/v1/admin/competitions/{response.Id}", response);
    }
}
