using NoCTF.API.Endpoints.Competitions.Events;

namespace NoCTF.Tests.Unit.Api;

public sealed class CompetitionEventHistoryRequestTests
{
    [Test]
    public async Task Validator_accepts_unbounded_staff_history_and_bounded_participant_history()
    {
        var validator = new ListCompetitionEventsValidator();
        var now = DateTimeOffset.UtcNow;

        var unbounded = await validator.ValidateAsync(new ListCompetitionEventsRequest());
        var bounded = await validator.ValidateAsync(new ListCompetitionEventsRequest
        {
            From = now.AddDays(-30),
            To = now
        });
        var incomplete = await validator.ValidateAsync(new ListCompetitionEventsRequest
        {
            From = now.AddDays(-1)
        });
        var tooWide = await validator.ValidateAsync(new ListCompetitionEventsRequest
        {
            From = now.AddDays(-32),
            To = now
        });

        await Assert.That(unbounded.IsValid).IsTrue();
        await Assert.That(bounded.IsValid).IsTrue();
        await Assert.That(incomplete.IsValid).IsFalse();
        await Assert.That(tooWide.IsValid).IsFalse();
    }
}
