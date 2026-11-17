using NoCTF.Application.Competitions.Credentials;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class CompetitionCredentialImportPolicyTests
{
    [Test]
    public async Task Enabled_tracks_require_a_valid_track_for_every_member()
    {
        var competition = Competition();
        competition.TracksEnabled = true;
        var issues = CompetitionCredentialImportPolicy.ValidateRows([new(2, "alice", null, "Blue", true, null)], competition);
        await Assert.That(issues.Single().Code).IsEqualTo(CompetitionCredentialImportIssueCode.InvalidTrackKey);
        await Assert.That(CompetitionCredentialImportPolicy.ValidateRows([new(2, "alice", null, "Blue", true, " DEFAULT ")], competition)).IsEmpty();
    }

    [Test]
    public async Task Disabled_tracks_ignore_supplied_track_values()
    {
        var competition = Competition();
        var issues = CompetitionCredentialImportPolicy.ValidateRows([
            new(2, "alice", null, "Blue", true, "unknown"), new(3, "bob", null, "Blue", false, null)], competition);
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task Optional_identity_limits_are_checked_before_persistence()
    {
        var issues = CompetitionCredentialImportPolicy.ValidateRows([
            new(2, "alice", null, "Blue", true, null, new string('a', 101), new string('1', 65), new string('b', 201))], Competition());
        await Assert.That(issues.Count).IsEqualTo(3);
        await Assert.That(issues.All(issue => issue.RowNumber == 2 && issue.Code == CompetitionCredentialImportIssueCode.ValueTooLong)).IsTrue();
    }

    private static Competition Competition() => new CtfCompetition
    {
        MaxTeamMembers = 5,
        Tracks = CompetitionTrackConfiguration.ToPersisted(CompetitionTrackConfiguration.DefaultFor(GameMode.Ctf))
    };
}
