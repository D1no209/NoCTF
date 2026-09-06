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
    [Arguments("""{"mode":"Ctf","visibility":"Private"}""")]
    public async Task Update_request_accepts_pascal_case_string_enums(string json)
    {
        var request = JsonSerializer.Deserialize<UpdateChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Mode!.Value).IsEqualTo(GameModeProtocol.Ctf);
        await Assert.That(request.Visibility!.Value).IsEqualTo(ChallengeVisibilityProtocol.Private);
    }

    [Test]
    public async Task Update_request_requires_complete_last_write_wins_payload()
    {
        var validator = new UpdateChallengeTemplateValidator();
        var missing = validator.Validate(new UpdateChallengeTemplateRequest());
        var missingProperties = missing.Errors
            .Select(error => error.PropertyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Assert.That(missingProperties.Contains(
            nameof(UpdateChallengeTemplateRequest.Mode))).IsTrue();
        await Assert.That(missingProperties.Contains(
            nameof(UpdateChallengeTemplateRequest.Visibility))).IsTrue();
        await Assert.That(missingProperties.Contains(
            nameof(UpdateChallengeTemplateRequest.Title))).IsTrue();
        await Assert.That(missingProperties.Contains(
            nameof(UpdateChallengeTemplateRequest.Direction))).IsTrue();
        await Assert.That(missingProperties.Contains(
            nameof(UpdateChallengeTemplateRequest.DefinitionJson))).IsFalse();
        await Assert.That(validator.Validate(new UpdateChallengeTemplateRequest
        {
            Mode = GameModeProtocol.Ctf,
            Visibility = ChallengeVisibilityProtocol.Private,
            Title = "Template",
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":1}"""
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
    public async Task Update_result_mapping_keeps_runtime_statuses_typed()
    {
        var notFound = ChallengeTemplateUpdateResponseMapper.ToResponse(
            new(ChallengeTemplateWriteState.NotFoundOrForbidden));
        var activeMode = ChallengeTemplateUpdateResponseMapper.ToResponse(
            new(ChallengeTemplateWriteState.ActiveCompetitionModeConflict));
        var activeRuntime = ChallengeTemplateUpdateResponseMapper.ToResponse(
            new(ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict));
        var invalidDefinition = ChallengeTemplateUpdateResponseMapper.ToResponse(
            new(
                ChallengeTemplateWriteState.InvalidDefinition,
                Detail: "Definition is invalid."));

        await Assert.That(notFound.Result).IsTypeOf<NotFound>();
        await Assert.That(activeMode.Result)
            .IsTypeOf<Conflict<ChallengeTemplateConflictResponse>>();
        var activeModeConflict =
            (Conflict<ChallengeTemplateConflictResponse>)activeMode.Result;
        await Assert.That(activeModeConflict.Value!.Code)
            .IsEqualTo(ChallengeTemplateConflictCode.ActiveCompetitionModeConflict);
        await Assert.That(activeRuntime.Result)
            .IsTypeOf<Conflict<ChallengeTemplateConflictResponse>>();
        var activeRuntimeConflict =
            (Conflict<ChallengeTemplateConflictResponse>)activeRuntime.Result;
        await Assert.That(activeRuntimeConflict.Value!.Code)
            .IsEqualTo(ChallengeTemplateConflictCode.ActiveRuntimeDefinitionConflict);
        await Assert.That(invalidDefinition.Result)
            .IsTypeOf<ProblemHttpResult>();
        var response = (ProblemHttpResult)invalidDefinition.Result;
        var value = (ValidationProblemDetails)response.ProblemDetails;
        await Assert.That(value.Status).IsEqualTo(400);
        await Assert.That(value.Errors.Keys)
            .Contains(nameof(UpdateChallengeTemplateRequest.DefinitionJson));
    }
}
