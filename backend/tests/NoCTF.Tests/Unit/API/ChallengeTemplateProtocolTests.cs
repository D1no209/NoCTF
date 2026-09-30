using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NoCTF.Application.Challenges.Bank;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Api;

public sealed class ChallengeTemplateProtocolTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("""{"mode":"Ctf","visibility":"Private","definition":{"mode":"Ctf","ctf":{"interactionKind":"FlagSubmission"},"patchCommand":[]}}""")]
    public async Task Create_request_accepts_pascal_case_string_enums(string json)
    {
        var request = JsonSerializer.Deserialize<CreateChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Mode).IsEqualTo(GameModeProtocol.Ctf);
        await Assert.That(request.Visibility).IsEqualTo(ChallengeVisibilityProtocol.Private);
    }

    [Test]
    [Arguments("""{"content":{"mode":"Ctf","visibility":"Private","title":"Template","description":null,"direction":"Web","definition":{"mode":"Ctf","ctf":{"interactionKind":"FlagSubmission"},"patchCommand":[]}}}""")]
    public async Task Update_request_accepts_pascal_case_string_enums(string json)
    {
        var request = JsonSerializer.Deserialize<PatchChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Content!.Mode).IsEqualTo(GameModeProtocol.Ctf);
        await Assert.That(request.Content.Visibility).IsEqualTo(ChallengeVisibilityProtocol.Private);
    }

    [Test]
    [Arguments("""{"ctf":{"defaultScoreCurve":{},"bloodRewards":[]},"flagTemplate":{"header":"flag","bodyTemplate":"[GUID]","leetLiteralText":false},"mode":"Ctf"}""", true)]
    [Arguments("""{"awd":{},"flagTemplate":{"header":"flag","bodyTemplate":"[GUID]","leetLiteralText":false},"mode":"Ctf"}""", false)]
    [Arguments("""{"ctf":{"defaultScoreCurve":{},"bloodRewards":[]},"awd":{},"flagTemplate":{"header":"flag","bodyTemplate":"[GUID]","leetLiteralText":false},"mode":"Ctf"}""", false)]
    [Arguments("""{"ctf":{"defaultScoreCurve":null,"bloodRewards":[]},"flagTemplate":{"header":"flag","bodyTemplate":"[GUID]","leetLiteralText":false},"mode":"Ctf"}""", false)]
    public async Task Configuration_branch_is_validated_independently_of_json_order(string json, bool valid)
    {
        var contract = JsonSerializer.Deserialize<CompetitionModeConfigurationContract>(json, JsonOptions);

        await Assert.That(CompetitionModeConfigurationContractMapper.HasValidShape(contract)).IsEqualTo(valid);
    }

    [Test]
    [Arguments("""{"ctf":{},"mode":"Ctf"}""", true)]
    [Arguments("""{"awd":{},"mode":"Ctf"}""", false)]
    [Arguments("""{"ctf":{},"awd":{},"mode":"Ctf"}""", false)]
    public async Task Rules_branch_is_validated_independently_of_json_order(string json, bool valid)
    {
        var contract = JsonSerializer.Deserialize<CompetitionChallengeRulesContract>(json, JsonOptions);

        await Assert.That(CompetitionChallengeRulesContractMapper.HasValidShape(contract)).IsEqualTo(valid);
    }

    [Test]
    [Arguments("""{"ova":{"sourceUrl":"https://example.test/a.ova","sha256":"abc"},"limits":{"memoryBytes":512,"cpuMillicores":500,"pidsLimit":256},"kind":"Ova"}""")]
    [Arguments("""{"container":{"services":[{"name":"main","image":"alpine"}]},"kind":"Container"}""")]
    public async Task Runtime_branch_is_validated_with_kind_after_payload(string json)
    {
        var contract = JsonSerializer.Deserialize<ChallengeRuntimeContract>(json, JsonOptions);

        await Assert.That(ChallengeDefinitionContractMapper.HasValidRuntimeShape(contract)).IsTrue();
    }

    [Test]
    public async Task Update_request_requires_complete_last_write_wins_payload()
    {
        var validator = new PatchChallengeTemplateValidator();
        var missing = validator.Validate(new PatchChallengeTemplateRequest
        {
            Content = new()
            {
                Mode = (GameModeProtocol)(-1),
                Visibility = (ChallengeVisibilityProtocol)(-1),
                Title = string.Empty,
                Description = null,
                Direction = string.Empty,
                Definition = CtfDefinition()
            }
        });
        var missingProperties = missing.Errors
            .Select(error => error.PropertyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Assert.That(missingProperties.Contains(
            "Content.Mode")).IsTrue();
        await Assert.That(missingProperties.Contains(
            "Content.Visibility")).IsTrue();
        await Assert.That(missingProperties.Contains(
            "Content.Title")).IsTrue();
        await Assert.That(missingProperties.Contains(
            "Content.Direction")).IsTrue();
        await Assert.That(validator.Validate(new PatchChallengeTemplateRequest
        {
            Content = new()
            {
                Mode = GameModeProtocol.Ctf,
                Visibility = ChallengeVisibilityProtocol.Private,
                Title = "Template",
                Description = null,
                Direction = "Web",
                Definition = CtfDefinition()
            }
        }).IsValid).IsTrue();
    }

    [Test]
    [Arguments(ChallengeTemplateConflictCode.RoleNotEligible)]
    [Arguments(ChallengeTemplateConflictCode.ActiveCompetitionModeConflict)]
    [Arguments(ChallengeTemplateConflictCode.ActiveRuntimeDefinitionConflict)]
    public async Task Conflict_code_serializes_as_a_named_enum(
        ChallengeTemplateConflictCode code)
    {
        var json = JsonSerializer.Serialize(code, JsonOptions);

        await Assert.That(json).IsEqualTo($"\"{code}\"");
    }

    [Test]
    public async Task Permission_specific_mappers_only_update_their_allowed_fields()
    {
        var challenge = new CtfChallenge
        {
            Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), ManagerIds = [],
            Visibility = ChallengeVisibility.Private,
            Title = "Old", Direction = "Web", Definition = TestConfigurations.Definition(GameMode.Ctf)
        };
        ChallengeTemplatePatchMapper.ApplyContentAsTemplateManager(new()
        {
            Mode = GameModeProtocol.Awd,
            Visibility = ChallengeVisibilityProtocol.Shared,
            Title = "New",
            Description = null,
            Direction = "Pwn",
            Definition = AwdDefinition()
        }, challenge);
        var ownerAfterContent = challenge.OwnerId;
        var newOwner = Guid.NewGuid();
        ChallengeTemplatePatchMapper.ApplyPermissionsAsTemplateOwner(new()
        {
            OwnerId = newOwner,
            ManagerIds = [challenge.OwnerId]
        }, challenge);

        await Assert.That(challenge.Title).IsEqualTo("New");
        await Assert.That(challenge.OwnerId).IsEqualTo(newOwner);
        await Assert.That(ownerAfterContent).IsNotEqualTo(newOwner);
    }

    private static ChallengeDefinitionContract CtfDefinition() => new()
    {
        Mode = GameModeProtocol.Ctf,
        Ctf = new CtfChallengeDefinitionContract
        {
            InteractionKind = NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol.FlagSubmission
        },
        PatchCommand = []
    };

    private static ChallengeDefinitionContract AwdDefinition() => new()
    {
        Mode = GameModeProtocol.Awd,
        Awd = new AwdChallengeDefinitionContract { FlagInjection = null },
        PatchCommand = []
    };
}
