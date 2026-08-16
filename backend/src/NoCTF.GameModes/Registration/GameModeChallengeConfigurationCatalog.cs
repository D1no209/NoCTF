using System.Text.Json;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeChallengeConfigurationCatalog : IChallengeConfigurationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GetDefaultJson(GameMode mode) =>
        JsonSerializer.Serialize(
            new { schemaVersion = CurrentSchemaVersion(mode) },
            JsonOptions);

    public string GetDefaultDefinitionJson(GameMode mode) => GetDefaultJson(mode);

    public IReadOnlyList<string> ValidateRules(
        GameMode mode,
        string json,
        string competitionConfigurationJson,
        int eligibleTeamCount) =>
        [
            .. ValidateOwnedProperties(mode, json, DefinitionProperties(mode), "RulesJson"),
            .. Validate(mode, json, competitionConfigurationJson, eligibleTeamCount)
        ];

    public IReadOnlyList<string> ValidateDefinition(GameMode mode, string json) =>
        [
            .. ValidateOwnedProperties(mode, json, RuleProperties(mode), "DefinitionJson"),
            .. Validate(
                mode,
                json,
                GameModeDefaultConfiguration.GetCompetitionJson(mode),
                1)
        ];

    public IReadOnlyList<string> ValidateDefinitionForStart(GameMode mode, string json)
    {
        var errors = ValidateDefinition(mode, json).ToList();
        if (errors.Count > 0 || mode != GameMode.Awd)
            return errors;

        if (AwdConfigurationUpgrader.ParseChallenge(json).Runtime is null)
            errors.Add("Runtime is required before an AWD competition can start.");
        return errors;
    }

    private static int CurrentSchemaVersion(GameMode mode) => mode switch
    {
        GameMode.Ctf => CtfChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Awd => AwdChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Awdp => AwdpChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Koh => KohChallengeConfiguration.CurrentSchemaVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };

    public IReadOnlyList<string> Validate(GameMode mode, string json) =>
        Validate(mode, json, GameModeDefaultConfiguration.GetCompetitionJson(mode), 1);

    public IReadOnlyList<string> Validate(
        GameMode mode,
        string json,
        string competitionConfigurationJson,
        int eligibleTeamCount)
    {
        try
        {
            return mode switch
            {
                GameMode.Ctf => CtfConfigurationValidator.Validate(
                    CtfConfigurationUpgrader.ParseChallenge(json),
                    CtfConfigurationUpgrader.ParseCompetition(competitionConfigurationJson),
                    eligibleTeamCount),
                GameMode.Awd => AwdConfigurationValidator.Validate(AwdConfigurationUpgrader.ParseChallenge(json)),
                GameMode.Awdp => ValidateAwdp(json, competitionConfigurationJson),
                GameMode.Koh => KohConfigurationValidator.Validate(KohConfigurationUpgrader.ParseChallenge(json)),
                _ => ["Unsupported game mode."]
            };
        }
        catch (Exception exception) when (exception is GameModeConfigurationException or JsonException)
        {
            return [exception.Message];
        }
    }

    private static IReadOnlyList<string> ValidateAwdp(
        string json,
        string competitionConfigurationJson)
    {
        var competition = AwdpConfigurationParser.ParseCompetition(
            competitionConfigurationJson);
        var challenge = AwdpConfigurationParser.ParseChallenge(json);
        var competitionErrors = AwdpConfigurationValidator.Validate(competition);
        if (competitionErrors.Count > 0)
            return competitionErrors;
        return
        [
            .. AwdpConfigurationValidator.Validate(challenge),
            .. AwdpConfigurationValidator.Validate(
                AwdpConfigurationResolver.Resolve(competition, challenge))
        ];
    }

    private static IReadOnlyList<string> ValidateOwnedProperties(
        GameMode mode,
        string json,
        IReadOnlySet<string> forbidden,
        string section)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [$"{section} must be a JSON object."];
            return document.RootElement.EnumerateObject()
                .Where(property => forbidden.Contains(property.Name))
                .Select(property =>
                    $"{section} cannot contain '{property.Name}' because it belongs to the other challenge section.")
                .ToArray();
        }
        catch (JsonException exception)
        {
            return [exception.Message];
        }
    }

    private static IReadOnlySet<string> DefinitionProperties(GameMode mode) =>
        new HashSet<string>(
            mode switch
            {
                GameMode.Ctf => ["runtime"],
                GameMode.Awd => ["runtime", "checker", "flagInjection"],
                GameMode.Awdp =>
                [
                    "runtime",
                    "patchEntrypoint",
                    "patchCommand",
                    "patchTimeoutSeconds",
                    "checker",
                    "readyTimeoutSeconds",
                    "flagInjection"
                ],
                GameMode.Koh => ["runtime"],
                _ => []
            },
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> RuleProperties(GameMode mode) =>
        new HashSet<string>(
            mode switch
            {
                GameMode.Ctf =>
                [
                    "points",
                    "bloodRewards",
                    "maxFlagAttempts",
                    "scoreExpression",
                    "wrongSubmissionPenalty",
                    "flagTemplate"
                ],
                GameMode.Awd =>
                [
                    "attackRewardMode",
                    "attackPoints",
                    "victimDefensePoolPoints",
                    "checkerIntervalSeconds",
                    "serviceHealthyPoints",
                    "serviceUnhealthyPenalty",
                    "flagTemplate"
                ],
                GameMode.Awdp =>
                [
                    "break",
                    "fix",
                    "requireBreakBeforeFix",
                    "maxBreakSubmissions",
                    "maxFixSubmissions",
                    "breakWrongPenalty",
                    "fixFailurePenalty",
                    "violationPenalty",
                    "serviceDownPenalty",
                    "evaluationDispatchMode",
                    "flagTemplate"
                ],
                GameMode.Koh => ["pollIntervalSeconds", "controlPointsPerInterval"],
                _ => []
            },
            StringComparer.OrdinalIgnoreCase);
}
