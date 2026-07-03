using FastEndpoints;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class CreateCompetitionAdminRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GameModeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public PointsConfigDto? DefaultPointsConfig { get; set; }
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

public class CreateCompetitionAdminEndpoint(ApplicationDbContext dbContext)
    : Endpoint<CreateCompetitionAdminRequest, CompetitionSummaryDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CreateCompetitionAdminRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdClaim, out var userId);

        var mode = Enum.TryParse<GameModeType>(req.GameModeType, ignoreCase: true, out var parsedMode)
            ? parsedMode
            : GameModeType.Ctf;

        var status = Enum.TryParse<CompetitionStatus>(req.Status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : CompetitionStatus.Draft;

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Title = req.Title,
            Description = req.Description,
            GameModeType = mode,
            ModeKey = CompetitionModeDefaults.GetModeKey(mode),
            ScoringProfileJson = ScoringJson.Serialize(CompetitionModeDefaults.GetScoringProfile(mode)),
            OwnerId = userId,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Status = status,
            DefaultInitialPoints = req.DefaultPointsConfig?.InitialPoints ?? 500,
            DefaultMinimumPoints = req.DefaultPointsConfig?.MinimumPoints ?? 100,
            DefaultDecayFactor = req.DefaultPointsConfig?.DecayFactor ?? 450,
            DefaultDecayFunction = req.DefaultPointsConfig?.DecayFunction ?? "sigmoid",
            DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? 1.0 : req.DifficultyCoefficient,
            FirstBloodBonusPercent = NormalizeBonusPercent(req.FirstBloodBonusPercent),
            SecondBloodBonusPercent = NormalizeBonusPercent(req.SecondBloodBonusPercent),
            ThirdBloodBonusPercent = NormalizeBonusPercent(req.ThirdBloodBonusPercent),
            TeamRegistrationAutoApprove = req.TeamRegistrationAutoApprove,
            MaxTeamMembers = req.MaxTeamMembers <= 0 ? 5 : req.MaxTeamMembers,
            TracksEnabled = req.TracksEnabled,
            TrackNamesJson = System.Text.Json.JsonSerializer.Serialize(NormalizeTracks(req.TrackNames)),
            RoundDurationSeconds = NormalizePositive(req.RoundDurationSeconds),
            TotalRounds = NormalizePositive(req.TotalRounds),
            FlagFormat = string.IsNullOrWhiteSpace(req.FlagFormat) ? null : req.FlagFormat.Trim(),
            FlagPath = string.IsNullOrWhiteSpace(req.FlagPath) ? null : req.FlagPath.Trim(),
            AttackPoints = NormalizeNonNegative(req.AttackPoints),
            ServiceOnlinePoints = NormalizeNonNegative(req.ServiceOnlinePoints),
            ServiceDownPenalty = NormalizeNonNegative(req.ServiceDownPenalty),
            BeenAttackedPenalty = NormalizeNonNegative(req.BeenAttackedPenalty),
            FlagValidityRounds = NormalizePositive(req.FlagValidityRounds),
            AwdpAttackScorePerRound = req.AwdpAttackScorePerRound,
            AwdpDefenseScorePerRound = req.AwdpDefenseScorePerRound,
            AwdpMaxAttackAttempts = req.AwdpMaxAttackAttempts,
            AwdpMaxDefenseAttempts = req.AwdpMaxDefenseAttempts,
            AwdpAllowAttackAfterBreakSuccess = req.AwdpAllowAttackAfterBreakSuccess,
            AwdpAllowDefenseAfterFixSuccess = req.AwdpAllowDefenseAfterFixSuccess,
            AwdpServicePenaltyEnabled = req.AwdpServicePenaltyEnabled,
            AwdpServicePenaltyPerRound = req.AwdpServicePenaltyPerRound,
            AwdpViolationPenaltyEnabled = req.AwdpViolationPenaltyEnabled,
            AwdpViolationPenalty = req.AwdpViolationPenalty,
            AwdpFixEntry = string.IsNullOrWhiteSpace(req.AwdpFixEntry) ? null : req.AwdpFixEntry.Trim(),
            AwdpFixTimeoutSeconds = req.AwdpFixTimeoutSeconds,
        };
        competition.CompetitionId = competition.Id;

        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new CompetitionSummaryDto
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
            TrackNames = NormalizeTracks(req.TrackNames),
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
        }, 201, ct);
    }

    internal static List<string> NormalizeTracks(IEnumerable<string> tracks)
        => tracks
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static double NormalizeBonusPercent(double value)
        => double.IsFinite(value) && value > 0 ? Math.Min(1000, value) : 0;

    internal static int? NormalizePositive(int? value)
        => value is > 0 ? value : null;

    internal static int? NormalizeNonNegative(int? value)
        => value is >= 0 ? value : null;
}
