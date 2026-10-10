using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Challenges;

namespace NoCTF.Tests.Unit.API;

public sealed class ChallengeTimingPatchBindingTests
{
    [Test]
    public async Task Missing_time_fields_and_explicit_null_are_distinct_in_the_actual_request_contract()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var omitted = JsonSerializer.Deserialize<PatchCompetitionChallengeRequest>("{\"timing\":{\"scoringEndsAt\":null}}", options)!;
        await Assert.That(omitted.Timing!.HasAutoOpenAt).IsFalse();
        await Assert.That(omitted.Timing.HasScoringEndsAt).IsTrue();
        await Assert.That(omitted.Timing.ScoringEndsAt).IsNull();
        await Assert.That(omitted.Timing.HasSubmissionDeadlineAt).IsFalse();
        var explicitNull = JsonSerializer.Deserialize<PatchCompetitionChallengeRequest>("{\"timing\":{\"autoOpenAt\":null}}", options)!;
        await Assert.That(explicitNull.Timing!.HasAutoOpenAt).IsTrue();
    }
}
