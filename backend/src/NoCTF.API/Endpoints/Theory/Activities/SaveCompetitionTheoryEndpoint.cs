using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class SaveCompetitionTheoryRequest
{
    public Guid CompetitionId { get; set; } public Guid? ActivityId { get; set; } public required TheoryActivityInput Input { get; set; }
}
public sealed class SaveCompetitionTheoryValidator : Validator<SaveCompetitionTheoryRequest>
{
    public SaveCompetitionTheoryValidator()
    {
        RuleFor(request => request.CompetitionId).NotEmpty(); RuleFor(request => request.Input).NotNull();
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new TheoryActivityInputValidator()));
    }
}
public sealed class SaveCompetitionTheoryEndpoint(ITheoryActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<SaveCompetitionTheoryRequest, Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/theory"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse())); MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("SaveCompetitionTheory"));
    }
    public override async Task<Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>> ExecuteAsync(SaveCompetitionTheoryRequest request, CancellationToken ct)
        => TheoryActivityHttp.Result(await store.SaveAsync(new(user.UserId, request.ActivityId, request.CompetitionId, request.Input, clock.GetUtcNow()), ct));
}
