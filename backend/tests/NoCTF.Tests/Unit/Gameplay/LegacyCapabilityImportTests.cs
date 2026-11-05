using System.Text.Json;
using NoCTF.CurrentImport;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class LegacyCapabilityImportTests
{
    [Test]
    [Arguments("0", RuntimePurpose.Player)]
    [Arguments("1", RuntimePurpose.FixTarget)]
    [Arguments("2", RuntimePurpose.Practice)]
    [Arguments("3", RuntimePurpose.AttackTarget)]
    [Arguments("4", RuntimePurpose.TemplateTest)]
    [Arguments("\"Player\"", RuntimePurpose.Player)]
    [Arguments("\"AwdpTarget\"", RuntimePurpose.FixTarget)]
    [Arguments("\"Practice\"", RuntimePurpose.Practice)]
    [Arguments("\"AwdpAttack\"", RuntimePurpose.AttackTarget)]
    [Arguments("\"TemplateTest\"", RuntimePurpose.TemplateTest)]
    public async Task Archive_runtime_purposes_map_to_resource_uses(string json, RuntimePurpose expected)
    {
        using var document = JsonDocument.Parse(json);
        await Assert.That(LegacyConverters.RuntimeUse(document.RootElement)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("5")]
    [Arguments("\"PatchVerificationTarget\"")]
    [Arguments("99")]
    public async Task Removed_ctf_repair_resources_and_unknown_purposes_are_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        await Assert.That(() => LegacyConverters.RuntimeUse(document.RootElement)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("0", GameplayFactKind.BreakAttempt)]
    [Arguments("1", GameplayFactKind.BreakAttempt)]
    [Arguments("2", GameplayFactKind.FixAttempt)]
    [Arguments("3", GameplayFactKind.HintUnlock)]
    [Arguments("4", GameplayFactKind.ManualAdjustment)]
    [Arguments("5", GameplayFactKind.CheckObservation)]
    [Arguments("6", GameplayFactKind.ControlObservation)]
    [Arguments("7", GameplayFactKind.AttachmentDownload)]
    [Arguments("8", GameplayFactKind.WriteUpUnlock)]
    [Arguments("\"FlagAttempt\"", GameplayFactKind.BreakAttempt)]
    [Arguments("\"BreakAttempt\"", GameplayFactKind.BreakAttempt)]
    [Arguments("\"FixAttempt\"", GameplayFactKind.FixAttempt)]
    [Arguments("\"HintUnlock\"", GameplayFactKind.HintUnlock)]
    [Arguments("\"ManualAdjustment\"", GameplayFactKind.ManualAdjustment)]
    [Arguments("\"AwdServiceTransition\"", GameplayFactKind.CheckObservation)]
    [Arguments("\"KohControlObservation\"", GameplayFactKind.ControlObservation)]
    [Arguments("\"AttachmentDownload\"", GameplayFactKind.AttachmentDownload)]
    [Arguments("\"WriteUpUnlock\"", GameplayFactKind.WriteUpUnlock)]
    public async Task Archive_kinds_map_by_the_archive_contract_independently_of_current_enum_ordinals(string json, GameplayFactKind expected)
    {
        using var document = JsonDocument.Parse(json);
        await Assert.That(LegacyConverters.GameplayKind(document.RootElement)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("9")]
    [Arguments("\"TheoryPaperSubmission\"")]
    [Arguments("99")]
    public async Task Unassigned_questionnaires_and_unknown_kinds_are_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        await Assert.That(() => LegacyConverters.GameplayKind(document.RootElement)).Throws<InvalidOperationException>();
    }
}
