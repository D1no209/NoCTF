namespace NoCTF.API.Endpoints.Challenges;

public sealed class CreateChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = "Uncategorized";
    public int Order { get; set; }
}

public sealed class UpdateChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = "Uncategorized";
    public int Order { get; set; }
}

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    string Title,
    string? Description,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ChallengeListResponse(IReadOnlyList<ChallengeResponse> Items);
