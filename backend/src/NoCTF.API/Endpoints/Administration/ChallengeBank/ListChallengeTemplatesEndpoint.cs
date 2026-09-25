using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Endpoints.Competitions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeTemplateSummaryResponse(
    Guid Id,
    GameModeProtocol Mode,
    ChallengeVisibilityProtocol Visibility,
    string Title,
    string Direction,
    DateTimeOffset? DeletedAt,
    int ActiveCompetitionReferenceCount,
    DateTimeOffset UpdatedAt,
    CtfInteractionKindProtocol InteractionKind);

public sealed class ChallengeTemplateListResponse : ArrayResult<ChallengeTemplateSummaryResponse>
{
    public ChallengeTemplateListResponse() { }

    public ChallengeTemplateListResponse(
        ChallengeTemplateSummaryResponse[] items,
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
        TypedResults.Ok(ToListResponse(
            await list.ExecutePageAsync(new(
                user.UserId,
                user.IsAdministrator,
                request.IncludeDeleted,
                PaginationRules.Normalize(request.Keyword),
                PaginationRules.Normalize(request.Direction),
                request.Offset,
                request.Limit,
                request.Desc), ct)));

    private static ChallengeTemplateListResponse ToListResponse(ChallengeTemplateListPage page) =>
        new(page.Items.Select(item => new ChallengeTemplateSummaryResponse(
            item.Id,
            CompetitionProtocolMapper.ToProtocol(item.Mode),
            ChallengeTemplateMapper.ToProtocol(item.Visibility),
            item.Title,
            item.Direction,
            item.DeletedAt,
            item.ActiveCompetitionReferenceCount,
            item.UpdatedAt,
            ChallengeMapper.ToProtocol(item.InteractionKind))).ToArray(),
            page.Total,
            page.Directions);
}
