using System.Text;
using NoCTF.Infrastructure.Competitions.Credentials;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class CompetitionCredentialImportReaderTests
{
    [Test]
    public async Task Excel_workbook_reads_boolean_cells_and_optional_identity_text()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Credentials", "participants.xlsx"));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, "participants.xlsx", CancellationToken.None);
        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Rows).Count().IsEqualTo(2);
        await Assert.That(result.Rows[0].Captain).IsTrue();
        await Assert.That(result.Rows[1].Captain).IsFalse();
        await Assert.That(result.Rows[0].FullName).IsEqualTo("张三");
        await Assert.That(result.Rows[0].StudentNumber).IsEqualTo("001234");
        await Assert.That(result.Rows[0].School).IsEqualTo("成都理工大学");
    }

    [Test]
    public async Task Optional_identity_fields_preserve_leading_zeroes_and_empty_rows_keep_source_line_numbers()
    {
        const string csv = "用户名,队伍名称,是否队长,姓名,学号,学校\r\n" +
                           "alice,蓝队,是,张三,001234,成都理工大学\r\n,,,,,\r\nbob,蓝队,否,,,\r\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, "名单.csv", CancellationToken.None);
        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Rows).Count().IsEqualTo(2);
        await Assert.That(result.Rows[0].FullName).IsEqualTo("张三");
        await Assert.That(result.Rows[0].StudentNumber).IsEqualTo("001234");
        await Assert.That(result.Rows[0].School).IsEqualTo("成都理工大学");
        await Assert.That(result.Rows[1].RowNumber).IsEqualTo(4);
        await Assert.That(result.Rows[1].FullName).IsNull();
    }

    [Test]
    public async Task Duplicate_header_aliases_are_rejected()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("username,账号,teamName,captain\nalice,bob,Blue,true"));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, "accounts.csv", CancellationToken.None);
        await Assert.That(result.Rows).IsEmpty();
        await Assert.That(result.Issues.Single().Code).IsEqualTo(NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportIssueCode.DuplicateColumn);
    }

    [Test]
    [Arguments("accounts.exe", NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportIssueCode.UnsupportedFile)]
    [Arguments("accounts.xlsx", NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportIssueCode.InvalidFile)]
    public async Task Invalid_files_return_structured_issues(string name, NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportIssueCode expected)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not an Excel workbook"));
        var result = await new CompetitionCredentialImportReader().ReadAsync(stream, name, CancellationToken.None);
        await Assert.That(result.Rows).IsEmpty();
        await Assert.That(result.Issues.Single().Code).IsEqualTo(expected);
    }

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
