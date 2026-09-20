using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class ChallengeTemplateListResponse : ArrayResult<ChallengeTemplateResponse>
{
    public ChallengeTemplateListResponse() { }

    public ChallengeTemplateListResponse(
        ChallengeTemplateResponse[] items,
        int total,
        IReadOnlyList<string> directions)
        : base(items, total) =>
        Directions = directions;

    public IReadOnlyList<string> Directions { get; set; } = [];
}

public sealed class ListChallengeTemplatesRequest : SearchRequest
{
    [QueryParam]
    public bool IncludeDeleted { get; set; }

    [QueryParam]
    public string? Direction { get; set; }
}

public sealed class ListChallengeTemplatesValidator : Validator<ListChallengeTemplatesRequest>
{
    public ListChallengeTemplatesValidator()
    {
        PaginationRules.AddSearch(this);
        RuleFor(request => request.Direction).MaximumLength(96);
    }
}

public sealed class ListChallengeTemplatesEndpoint(
    ListChallengeTemplates list,
    IUserContext user)
    : Endpoint<ListChallengeTemplatesRequest, Ok<ChallengeTemplateListResponse>>
{
    public override void Configure()
    {
        Get("/admin/challenges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankListTemplates"));
        Summary(summary =>
        {
            summary.Summary = "Lists visible challenge templates.";
            summary.Description = "Returns owned, managed, shared, or platform-administrator-visible templates.";
        });
    }

    public override async Task<Ok<ChallengeTemplateListResponse>> ExecuteAsync(
        ListChallengeTemplatesRequest request,
        CancellationToken ct) =>
        TypedResults.Ok(ChallengeTemplateMapper.ToListResponse(
            await list.ExecutePageAsync(new(
                user.UserId,
                user.IsAdministrator,
                request.IncludeDeleted,
                PaginationRules.Normalize(request.Keyword),
                PaginationRules.Normalize(request.Direction),
                request.Offset,
                request.Limit,
                request.Desc), ct)));
}
