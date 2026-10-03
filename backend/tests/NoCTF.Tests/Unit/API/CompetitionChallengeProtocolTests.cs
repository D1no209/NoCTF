using NoCTF.API.Endpoints.Administration.Challenges;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionChallengeProtocolTests
{
    [Test]
    public async Task Tag_patch_distinguishes_preserve_clear_and_replace_in_json()
    {
        var tagProperties = new[] { "", ",\"tags\":null", ",\"tags\":[]", ",\"tags\":[\"Web\",\"SQL\"]" };
        var expectedCounts = new[] { -1, -1, 0, 2 };
        for (var index = 0; index < tagProperties.Length; index++)
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<CompetitionChallengePresentationPatchRequest>(
                "{\"customTitle\":null,\"order\":0,\"isPublished\":false" + tagProperties[index] + "}",
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            await Assert.That(parsed!.Tags?.Count ?? -1).IsEqualTo(expectedCounts[index]);
        }
        var request = new PatchCompetitionChallengeRequest { Presentation = new()
        { CustomTitle = null, Order = 0, IsPublished = false, Tags = [] } };
        var validator = new PatchCompetitionChallengeValidator();
        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.Presentation.Tags = ["Web", "SQL"];
        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.Presentation.Tags = [" "];
        await Assert.That(validator.Validate(request).IsValid).IsFalse();
    }

    [Test]
    public async Task Update_request_requires_the_complete_last_write_wins_shape()
    {
        var validator = new PatchCompetitionChallengeValidator();

        await Assert.That(validator.Validate(new PatchCompetitionChallengeRequest()).IsValid)
            .IsFalse();
        await Assert.That(validator.Validate(new PatchCompetitionChallengeRequest
        {
            Presentation = new()
            {
                CustomTitle = "Finals Web", Order = 1, IsPublished = false
            }
        }).IsValid).IsTrue();
        await Assert.That(validator.Validate(new PatchCompetitionChallengeRequest
        {
            Presentation = new()
            {
                CustomTitle = new string('x', 161), Order = 1, IsPublished = false
            }
        }).IsValid).IsFalse();
    }


    [Test]
    public async Task Create_request_allows_blank_fallback_and_bounds_custom_title()
    {
        var validator = new CreateChallengeValidator();
        var request = new CreateChallengeRequest
        {
            ChallengeId = Guid.NewGuid(),
            Order = 1
        };

        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.CustomTitle = "Finals Web";
        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.CustomTitle = new string('x', 161);
        await Assert.That(validator.Validate(request).IsValid).IsFalse();
    }
}
