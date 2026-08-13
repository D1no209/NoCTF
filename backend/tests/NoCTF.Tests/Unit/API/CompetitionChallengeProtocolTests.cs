using NoCTF.API.Endpoints.Administration.Challenges;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionChallengeProtocolTests
{
    [Test]
    public async Task Lifecycle_requests_require_a_non_negative_revision()
    {
        var deleteValidator = new DeleteChallengeValidator();
        var restoreValidator = new RestoreChallengeValidator();

        await Assert.That(deleteValidator.Validate(new DeleteChallengeRequest()).IsValid)
            .IsFalse();
        await Assert.That(restoreValidator.Validate(new RestoreChallengeRequest()).IsValid)
            .IsFalse();
        await Assert.That(deleteValidator.Validate(new DeleteChallengeRequest
        {
            ExpectedRevision = 0
        }).IsValid).IsTrue();
        await Assert.That(restoreValidator.Validate(new RestoreChallengeRequest
        {
            ExpectedRevision = 0
        }).IsValid).IsTrue();
        await Assert.That(deleteValidator.Validate(new DeleteChallengeRequest
        {
            ExpectedRevision = -1
        }).IsValid).IsFalse();
        await Assert.That(restoreValidator.Validate(new RestoreChallengeRequest
        {
            ExpectedRevision = -1
        }).IsValid).IsFalse();
    }

    [Test]
    public async Task Update_request_requires_the_complete_revision_fenced_shape()
    {
        var validator = new UpdateChallengeValidator();

        await Assert.That(validator.Validate(new UpdateChallengeRequest { CustomTitle = null }).IsValid)
            .IsFalse();
        await Assert.That(validator.Validate(new UpdateChallengeRequest
        {
            CustomTitle = "Finals Web",
            BaseScore = 500,
            Order = 1,
            IsPublished = false,
            ExpectedRevision = 0
        }).IsValid).IsTrue();
        await Assert.That(validator.Validate(new UpdateChallengeRequest
        {
            CustomTitle = new string('x', 161),
            BaseScore = 500,
            Order = 1,
            IsPublished = false,
            ExpectedRevision = 0
        }).IsValid).IsFalse();
    }


    [Test]
    public async Task Create_request_allows_blank_fallback_and_bounds_custom_title()
    {
        var validator = new CreateChallengeValidator();
        var request = new CreateChallengeRequest
        {
            ChallengeId = Guid.NewGuid(),
            BaseScore = 100,
            Order = 1
        };

        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.CustomTitle = "Finals Web";
        await Assert.That(validator.Validate(request).IsValid).IsTrue();
        request.CustomTitle = new string('x', 161);
        await Assert.That(validator.Validate(request).IsValid).IsFalse();
    }
}
