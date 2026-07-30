using System.Text.Json;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Api;

public sealed class ChallengeTemplateProtocolTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("""{"mode":"Ctf","visibility":"Private"}""")]
    [Arguments("""{"mode":0,"visibility":0}""")]
    public async Task Create_request_accepts_text_and_legacy_integer_enums(string json)
    {
        var request = JsonSerializer.Deserialize<CreateChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Mode).IsEqualTo(GameMode.Ctf);
        await Assert.That(request.Visibility).IsEqualTo(ChallengeVisibility.Private);
    }

    [Test]
    [Arguments("""{"mode":"Ctf","visibility":"Private"}""")]
    [Arguments("""{"mode":0,"visibility":0}""")]
    public async Task Update_request_accepts_text_and_legacy_integer_enums(string json)
    {
        var request = JsonSerializer.Deserialize<UpdateChallengeTemplateRequest>(
            json,
            JsonOptions);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Mode).IsEqualTo(GameMode.Ctf);
        await Assert.That(request.Visibility).IsEqualTo(ChallengeVisibility.Private);
    }
}
