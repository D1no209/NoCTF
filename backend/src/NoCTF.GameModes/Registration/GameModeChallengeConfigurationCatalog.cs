using System.Text.Json;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
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
            new { schemaVersion = CurrentRulesSchemaVersion(mode) },
            JsonOptions);

    public string GetDefaultDefinitionJson(GameMode mode) =>
        JsonSerializer.Serialize(
            new { schemaVersion = CurrentDefinitionSchemaVersion(mode) },
            JsonOptions);

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

    public IReadOnlyList<string> ValidateRulesForDefinition(
        GameMode mode,
        string rulesJson,
        string definitionJson,
        string competitionConfigurationJson,
        int eligibleTeamCount)
    {
        var errors = ValidateRules(
            mode,
            rulesJson,
            competitionConfigurationJson,
            eligibleTeamCount).ToList();
        if (errors.Count > 0 || mode != GameMode.Ctf)
            return errors;

        try
        {
            var definition = CtfConfigurationUpgrader.ParseChallenge(definitionJson);
            var rules = CtfConfigurationUpgrader.ParseChallenge(rulesJson);
            if (definition.InteractionKind == CtfInteractionKind.PatchVerification)
            {
                if (rules.MaxFlagAttempts is not null)
                    errors.Add("PatchVerification rules cannot configure MaxFlagAttempts.");
                if (rules.FlagTemplate is not null)
                    errors.Add("PatchVerification rules cannot configure FlagTemplate.");
            }
            else if (rules.MaxPatchAttempts is not null)
            {
                errors.Add("FlagSubmission rules cannot configure MaxPatchAttempts.");
            }
        }
        catch (Exception exception) when (exception is GameModeConfigurationException or JsonException)
        {
            errors.Add(exception.Message);
        }
        return errors;
    }

    public IReadOnlyList<string> ValidateDefinitionForStart(GameMode mode, string json)
    {
        var errors = ValidateDefinition(mode, json).ToList();
        if (errors.Count > 0)
            return errors;

        if (mode == GameMode.Awd
            && AwdConfigurationUpgrader.ParseChallenge(json).Runtime is null)
            errors.Add("Runtime is required before an AWD competition can start.");
        if (mode == GameMode.Ctf)
        {
            var configuration = CtfConfigurationUpgrader.ParseChallenge(json);
            if (configuration.InteractionKind == CtfInteractionKind.PatchVerification)
            {
                if (configuration.Runtime is null)
                    errors.Add("Runtime is required before a CTF PatchVerification challenge can start.");
                if (configuration.Checker is null)
                    errors.Add("Checker is required before a CTF PatchVerification challenge can start.");
            }
        }
        return errors;
    }

    private static int CurrentDefinitionSchemaVersion(GameMode mode) => mode switch
    {
        GameMode.Ctf => CtfChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Awd => AwdChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Awdp => AwdpChallengeConfiguration.CurrentSchemaVersion,
        GameMode.Koh => KohChallengeConfiguration.CurrentSchemaVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };

    private static int CurrentRulesSchemaVersion(GameMode mode) => mode switch
    {
        GameMode.Ctf => CtfConfiguration.CurrentSchemaVersion,
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
                GameMode.Awdp => ValidateAwdp(
                    json,
                    competitionConfigurationJson,
                    eligibleTeamCount),
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
        string competitionConfigurationJson,
        int eligibleTeamCount)
    {
        var competition = AwdpConfigurationParser.ParseCompetition(
            competitionConfigurationJson);
        var challenge = AwdpConfigurationParser.ParseChallenge(json);
        var competitionErrors = AwdpConfigurationValidator.Validate(
            competition,
            eligibleTeamCount);
        if (competitionErrors.Count > 0)
            return competitionErrors;
        return
        [
            .. AwdpConfigurationValidator.Validate(challenge, eligibleTeamCount),
            .. AwdpConfigurationValidator.Validate(
                AwdpConfigurationResolver.Resolve(competition, challenge),
                eligibleTeamCount)
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
                GameMode.Ctf =>
                [
                    "interactionKind",
                    "runtime",
                    "patchEntrypoint",
                    "patchCommand",
                    "patchTimeoutSeconds",
                    "checker",
                    "readyTimeoutSeconds",
                    "maximumPatchUploadBytes",
                    "checkerFixInput",
                    "checkerAllowRoot"
                ],
                GameMode.Awd => ["runtime", "checker", "checkerAllowRoot", "flagInjection"],
                GameMode.Awdp =>
                [
                    "runtime",
                    "patchEntrypoint",
                    "patchCommand",
                    "patchTimeoutSeconds",
                    "checker",
                    "checkerFixInput",
                    "checkerAllowRoot",
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
                    "scoreCurve",
                    "bloodRewards",
                    "maxFlagAttempts",
                    "maxPatchAttempts",
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
                    "flagWrongPenalty",
                    "exploitSucceededPenalty",
                    "serviceAbnormalPenalty",
                    "evaluationDispatchMode",
                    "flagTemplate"
                ],
                GameMode.Koh => ["pollIntervalSeconds", "controlPointsPerInterval"],
                _ => []
            },
            StringComparer.OrdinalIgnoreCase);
}
