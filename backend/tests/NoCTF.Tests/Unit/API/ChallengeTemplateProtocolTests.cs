using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NoCTF.Application.Challenges.Bank;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Api;

public sealed class ChallengeTemplateProtocolTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("""{"mode":"Ctf","visibility":"Private"}""")]
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
    [Arguments("""{"content":{"mode":"Ctf","visibility":"Private","title":"Template","description":null,"direction":"Web","definitionJson":"{}"}}""")]
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
                DefinitionJson = string.Empty
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
                DefinitionJson = """{"schemaVersion":1}"""
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
        var challenge = new Challenge
        {
            Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), ManagerIds = [],
            Mode = GameMode.Ctf, Visibility = ChallengeVisibility.Private,
            Title = "Old", Direction = "Web", DefinitionJson = "{}"
        };
        ChallengeTemplatePatchMapper.ApplyContentAsTemplateManager(new()
        {
            Mode = GameModeProtocol.Awd,
            Visibility = ChallengeVisibilityProtocol.Shared,
            Title = "New",
            Description = null,
            Direction = "Pwn",
            DefinitionJson = "{}"
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
}
