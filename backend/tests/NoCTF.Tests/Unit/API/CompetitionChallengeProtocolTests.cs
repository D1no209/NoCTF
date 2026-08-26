using NoCTF.API.Endpoints.Administration.Challenges;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionChallengeProtocolTests
{
    [Test]
    public async Task Update_request_requires_the_complete_last_write_wins_shape()
    {
        var validator = new UpdateChallengeValidator();

        await Assert.That(validator.Validate(new UpdateChallengeRequest { CustomTitle = null }).IsValid)
            .IsFalse();
        await Assert.That(validator.Validate(new UpdateChallengeRequest
        {
            CustomTitle = "Finals Web",
            Order = 1,
            IsPublished = false
        }).IsValid).IsTrue();
        await Assert.That(validator.Validate(new UpdateChallengeRequest
        {
            CustomTitle = new string('x', 161),
            Order = 1,
            IsPublished = false
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
