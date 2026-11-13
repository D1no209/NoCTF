using System.Text;
using NoCTF.Infrastructure.Competitions.Credentials;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class CompetitionCredentialImportReaderTests
{
    [Test]
    public async Task Csv_rows_map_user_team_captain_and_optional_fields()
    {
        const string csv = "username,email,teamName,captain,trackKey\n" +
                           "alice,alice@example.com,Blue,true,advanced\n" +
                           "bob,,Blue,false,advanced\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, "accounts.csv", CancellationToken.None);

        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Rows).Count().IsEqualTo(2);
        await Assert.That(result.Rows[0].UserName).IsEqualTo("alice");
        await Assert.That(result.Rows[0].Captain).IsTrue();
        await Assert.That(result.Rows[1].Email).IsNull();
        await Assert.That(result.Rows[1].TrackKey).IsEqualTo("advanced");
    }

    [Test]
    public async Task Chinese_headers_and_invalid_captain_values_are_reported()
    {
        const string csv = "用户名,邮箱,队伍名,队长\n" +
                           "alice,,蓝队,未知\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, "accounts.csv", CancellationToken.None);

        await Assert.That(result.Rows).IsEmpty();
        await Assert.That(result.Issues).HasSingleItem();
        await Assert.That(result.Issues[0].Code).IsEqualTo(
            NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportIssueCode.InvalidBoolean);
    }
}
