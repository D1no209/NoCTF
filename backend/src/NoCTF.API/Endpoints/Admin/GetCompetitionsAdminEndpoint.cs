using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using System.Security.Claims;
using System.Text.Json;

namespace NoCTF.API.Endpoints.Admin;

public class CompetitionSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GameModeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public Guid OwnerId { get; set; }
    public PointsConfigDto DefaultPointsConfig { get; set; } = new();
    public double DifficultyCoefficient { get; set; } = 1.0;
    public double FirstBloodBonusPercent { get; set; }
    public double SecondBloodBonusPercent { get; set; }
    public double ThirdBloodBonusPercent { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public bool TracksEnabled { get; set; }
    public List<string> TrackNames { get; set; } = [];
    public int? RoundDurationSeconds { get; set; }
    public int? TotalRounds { get; set; }
    public string? FlagFormat { get; set; }
    public string? FlagPath { get; set; }
    public int? AttackPoints { get; set; }
    public int? ServiceOnlinePoints { get; set; }
    public int? ServiceDownPenalty { get; set; }
    public int? BeenAttackedPenalty { get; set; }
    public int? FlagValidityRounds { get; set; }
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public bool? AwdpAllowAttackAfterBreakSuccess { get; set; }
    public bool? AwdpAllowDefenseAfterFixSuccess { get; set; }
    public bool? AwdpServicePenaltyEnabled { get; set; }
    public int? AwdpServicePenaltyPerRound { get; set; }
    public bool? AwdpViolationPenaltyEnabled { get; set; }
    public int? AwdpViolationPenalty { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }
}

public class GetCompetitionsAdminEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<EmptyRequest, List<CompetitionSummaryDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var manageableIds = await permissions.GetManageableCompetitionIdsAsync(userId, ct);
        var competitions = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => manageableIds.Contains(c.Id))
            .ToListAsync(ct);

        await SendAsync(competitions.Select(GetCompetitionAdminEndpoint.ToDto).ToList(), cancellation: ct);
    }
}

public class GetCompetitionAdminEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : EndpointWithoutRequest<CompetitionSummaryDto>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(ToDto(competition), cancellation: ct);
    }

    internal static CompetitionSummaryDto ToDto(Competition competition) => new()
    {
        Id = competition.Id,
        Title = competition.Title,
        Description = competition.Description,
        GameModeType = competition.GameModeType.ToString(),
        Status = competition.Status.ToString().ToLowerInvariant(),
        StartTime = competition.StartTime,
        EndTime = competition.EndTime,
        OwnerId = competition.OwnerId,
        DefaultPointsConfig = new PointsConfigDto
        {
            InitialPoints = competition.DefaultInitialPoints,
            MinimumPoints = competition.DefaultMinimumPoints,
            DecayFactor = competition.DefaultDecayFactor,
            DecayFunction = competition.DefaultDecayFunction,
        },
        DifficultyCoefficient = competition.DifficultyCoefficient,
        FirstBloodBonusPercent = competition.FirstBloodBonusPercent,
        SecondBloodBonusPercent = competition.SecondBloodBonusPercent,
        ThirdBloodBonusPercent = competition.ThirdBloodBonusPercent,
        TeamRegistrationAutoApprove = competition.TeamRegistrationAutoApprove,
        MaxTeamMembers = competition.MaxTeamMembers,
        TracksEnabled = competition.TracksEnabled,
        TrackNames = ParseTracks(competition.TrackNamesJson),
        RoundDurationSeconds = competition.RoundDurationSeconds,
        TotalRounds = competition.TotalRounds,
        FlagFormat = competition.FlagFormat,
        FlagPath = competition.FlagPath,
        AttackPoints = competition.AttackPoints,
        ServiceOnlinePoints = competition.ServiceOnlinePoints,
        ServiceDownPenalty = competition.ServiceDownPenalty,
        BeenAttackedPenalty = competition.BeenAttackedPenalty,
        FlagValidityRounds = competition.FlagValidityRounds,
        AwdpAttackScorePerRound = competition.AwdpAttackScorePerRound,
        AwdpDefenseScorePerRound = competition.AwdpDefenseScorePerRound,
        AwdpMaxAttackAttempts = competition.AwdpMaxAttackAttempts,
        AwdpMaxDefenseAttempts = competition.AwdpMaxDefenseAttempts,
        AwdpAllowAttackAfterBreakSuccess = competition.AwdpAllowAttackAfterBreakSuccess,
        AwdpAllowDefenseAfterFixSuccess = competition.AwdpAllowDefenseAfterFixSuccess,
        AwdpServicePenaltyEnabled = competition.AwdpServicePenaltyEnabled,
        AwdpServicePenaltyPerRound = competition.AwdpServicePenaltyPerRound,
        AwdpViolationPenaltyEnabled = competition.AwdpViolationPenaltyEnabled,
        AwdpViolationPenalty = competition.AwdpViolationPenalty,
        AwdpFixEntry = competition.AwdpFixEntry,
        AwdpFixTimeoutSeconds = competition.AwdpFixTimeoutSeconds,
    };

    internal static List<string> ParseTracks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; }
        catch (JsonException) { return []; }
    }
}
