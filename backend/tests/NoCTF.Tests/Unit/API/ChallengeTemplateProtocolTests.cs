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
    [Arguments("""{"visibility":"Private","definition":{"capabilities":[{"kind":"Break","break":{}}]}}""")]
    public async Task Create_request_accepts_pascal_case_string_enums(string json)
    {
        var request = JsonSerializer.Deserialize<CreateChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Definition.Capabilities.Single().Kind).IsEqualTo(ChallengeCapabilityProtocol.Break);
        await Assert.That(request.Visibility).IsEqualTo(ChallengeVisibilityProtocol.Private);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        await Assert.That(serialized.RootElement.TryGetProperty("mode", out _)).IsFalse();
    }

    [Test]
    [Arguments("""{"content":{"visibility":"Private","title":"Template","description":null,"direction":"Web","definition":{"capabilities":[{"kind":"Break","break":{}}]}}}""")]
    public async Task Update_request_accepts_pascal_case_string_enums(string json)
    {
        var request = JsonSerializer.Deserialize<PatchChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Content!.Definition.Capabilities.Single().Kind).IsEqualTo(ChallengeCapabilityProtocol.Break);
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
                Visibility = (ChallengeVisibilityProtocol)(-1),
                Title = string.Empty,
                Description = null,
                Direction = string.Empty,
                Definition = BreakDefinition()
            }
        });
        var missingProperties = missing.Errors
            .Select(error => error.PropertyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                Visibility = ChallengeVisibilityProtocol.Private,
                Title = "Template",
                Description = null,
                Direction = "Web",
                Definition = BreakDefinition()
            }
        }).IsValid).IsTrue();
    }

    [Test]
    [Arguments(ChallengeTemplateConflictCode.RoleNotEligible)]
    [Arguments(ChallengeTemplateConflictCode.ReferencedCapabilityConflict)]
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
        var definition = new ChallengeDefinition { Capabilities = [new NoCTF.Domain.Challenges.Capabilities.BreakCapabilityDefinition()] };
        var challenge = new Challenge
        {
            Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), ManagerIds = [],
            Visibility = ChallengeVisibility.Private,
            Title = "Old", Direction = "Web", Definition = definition
        };
        ChallengeTemplatePatchMapper.ApplyContentAsTemplateManager(new()
        {
            Visibility = ChallengeVisibilityProtocol.Shared,
            Title = "New",
            Description = null,
            Direction = "Pwn",
            Definition = BreakDefinition()
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
        await Assert.That(ReferenceEquals(challenge.Definition, definition)).IsTrue();
    }

    [Test]
    [Arguments("""{"capabilities":[]}""", false)]
    [Arguments("""{"capabilities":[{"kind":"Break","break":{}},{"kind":"Fix","fix":{"targetServiceName":"web"}}]}""", true)]
    [Arguments("""{"capabilities":[{"kind":"Break","break":{}},{"kind":"Break","break":{}}]}""", false)]
    [Arguments("""{"capabilities":[{"kind":"Break","fix":{"targetServiceName":"web"}}]}""", false)]
    [Arguments("""{"capabilities":[{"kind":"Break","break":{},"fix":{"targetServiceName":"web"}}]}""", false)]
    public async Task Capability_contract_rejects_missing_duplicate_or_mismatched_branches(string json, bool valid)
    {
        var contract = JsonSerializer.Deserialize<ChallengeDefinitionContract>(json, JsonOptions);
        await Assert.That(ChallengeDefinitionContractMapper.HasValidShape(contract)).IsEqualTo(valid);
    }

    private static ChallengeDefinitionContract BreakDefinition() => new()
    {
        Capabilities = [new() { Kind = ChallengeCapabilityProtocol.Break, Break = new() }]
    };
}
