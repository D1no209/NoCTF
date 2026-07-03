using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class UpdateCompetitionAdminRequest
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

public class UpdateCompetitionEndpoint(ApplicationDbContext dbContext) : Endpoint<UpdateCompetitionAdminRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(UpdateCompetitionAdminRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var competition = await dbContext.Competitions.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        competition.Title = req.Title;
        competition.Description = req.Description;
        competition.StartTime = req.StartTime;
        competition.EndTime = req.EndTime;
        competition.DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? 1.0 : req.DifficultyCoefficient;
        competition.FirstBloodBonusPercent = NormalizeBonusPercent(req.FirstBloodBonusPercent);
        competition.SecondBloodBonusPercent = NormalizeBonusPercent(req.SecondBloodBonusPercent);
        competition.ThirdBloodBonusPercent = NormalizeBonusPercent(req.ThirdBloodBonusPercent);
        competition.TeamRegistrationAutoApprove = req.TeamRegistrationAutoApprove;
        competition.MaxTeamMembers = req.MaxTeamMembers <= 0 ? 5 : req.MaxTeamMembers;
        competition.TracksEnabled = req.TracksEnabled;
        competition.TrackNamesJson = System.Text.Json.JsonSerializer.Serialize(
            CreateCompetitionAdminEndpoint.NormalizeTracks(req.TrackNames));
        competition.RoundDurationSeconds = CreateCompetitionAdminEndpoint.NormalizePositive(req.RoundDurationSeconds);
        competition.TotalRounds = CreateCompetitionAdminEndpoint.NormalizePositive(req.TotalRounds);
        competition.FlagFormat = string.IsNullOrWhiteSpace(req.FlagFormat) ? null : req.FlagFormat.Trim();
        competition.FlagPath = string.IsNullOrWhiteSpace(req.FlagPath) ? null : req.FlagPath.Trim();
        competition.AttackPoints = CreateCompetitionAdminEndpoint.NormalizeNonNegative(req.AttackPoints);
        competition.ServiceOnlinePoints = CreateCompetitionAdminEndpoint.NormalizeNonNegative(req.ServiceOnlinePoints);
        competition.ServiceDownPenalty = CreateCompetitionAdminEndpoint.NormalizeNonNegative(req.ServiceDownPenalty);
        competition.BeenAttackedPenalty = CreateCompetitionAdminEndpoint.NormalizeNonNegative(req.BeenAttackedPenalty);
        competition.FlagValidityRounds = CreateCompetitionAdminEndpoint.NormalizePositive(req.FlagValidityRounds);
        competition.AwdpAttackScorePerRound = req.AwdpAttackScorePerRound;
        competition.AwdpDefenseScorePerRound = req.AwdpDefenseScorePerRound;
        competition.AwdpMaxAttackAttempts = req.AwdpMaxAttackAttempts;
        competition.AwdpMaxDefenseAttempts = req.AwdpMaxDefenseAttempts;
        competition.AwdpAllowAttackAfterBreakSuccess = req.AwdpAllowAttackAfterBreakSuccess;
        competition.AwdpAllowDefenseAfterFixSuccess = req.AwdpAllowDefenseAfterFixSuccess;
        competition.AwdpServicePenaltyEnabled = req.AwdpServicePenaltyEnabled;
        competition.AwdpServicePenaltyPerRound = req.AwdpServicePenaltyPerRound;
        competition.AwdpViolationPenaltyEnabled = req.AwdpViolationPenaltyEnabled;
        competition.AwdpViolationPenalty = req.AwdpViolationPenalty;
        competition.AwdpFixEntry = string.IsNullOrWhiteSpace(req.AwdpFixEntry) ? null : req.AwdpFixEntry.Trim();
        competition.AwdpFixTimeoutSeconds = req.AwdpFixTimeoutSeconds;

        if (req.DefaultPointsConfig is not null)
        {
            competition.DefaultInitialPoints = req.DefaultPointsConfig.InitialPoints;
            competition.DefaultMinimumPoints = req.DefaultPointsConfig.MinimumPoints;
            competition.DefaultDecayFactor = req.DefaultPointsConfig.DecayFactor;
            competition.DefaultDecayFunction = string.IsNullOrWhiteSpace(req.DefaultPointsConfig.DecayFunction)
                ? "sigmoid"
                : req.DefaultPointsConfig.DecayFunction;
        }

        if (Enum.TryParse<GameModeType>(req.GameModeType, ignoreCase: true, out var mode))
        {
            competition.GameModeType = mode;
            competition.ModeKey = CompetitionModeDefaults.GetModeKey(mode);
            competition.ScoringProfileJson = ScoringJson.Serialize(CompetitionModeDefaults.GetScoringProfile(mode));
        }

        if (Enum.TryParse<CompetitionStatus>(req.Status, ignoreCase: true, out var status))
            competition.Status = status;

        await dbContext.SaveChangesAsync(ct);
        await SendOkAsync(ct);
    }

    private static double NormalizeBonusPercent(double value)
        => double.IsFinite(value) && value > 0 ? Math.Min(1000, value) : 0;
}
