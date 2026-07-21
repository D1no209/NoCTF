using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionEndpoint(CreateCompetition create, IUserContext user)
    : Endpoint<CreateCompetitionRequest, Results<Created<CompetitionResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions"); AuthSchemes("Bearer"); }

    public override async Task<Results<Created<CompetitionResponse>, ProblemHttpResult>> ExecuteAsync(CreateCompetitionRequest request, CancellationToken ct)
    {
        var result = await create.ExecuteAsync(CompetitionMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow), ct);
        if (!result.Succeeded)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Competition was not created.", detail: result.ErrorMessage);
        var response = CompetitionMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/competitions/{response.Id}", response);
    }
}
