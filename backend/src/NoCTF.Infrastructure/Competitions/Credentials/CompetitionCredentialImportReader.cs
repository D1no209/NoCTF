using System.Data;
using System.Text;
using ExcelDataReader;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.Infrastructure.Competitions.Credentials;

public sealed record CompetitionCredentialImportReadResult(
    IReadOnlyList<CompetitionCredentialImportRow> Rows,
    IReadOnlyList<CompetitionCredentialImportIssue> Issues);

public interface ICompetitionCredentialImportReader
{
    Task<CompetitionCredentialImportReadResult> ReadAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken);
}

public sealed class CompetitionCredentialImportReader : ICompetitionCredentialImportReader
{
    private static readonly string[] RequiredColumns = ["username", "teamname", "captain"];

    static CompetitionCredentialImportReader() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public Task<CompetitionCredentialImportReadResult> ReadAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".csv" and not ".xlsx" and not ".xls")
            throw new InvalidDataException("Only CSV and Excel files are supported.");

        using var reader = extension == ".csv"
            ? ExcelReaderFactory.CreateCsvReader(content, new ExcelReaderConfiguration
            {
                AutodetectSeparators = [',', ';', '\t'],
                FallbackEncoding = Encoding.UTF8
            })
            : ExcelReaderFactory.CreateReader(content);
        var data = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        });
        if (data.Tables.Count == 0)
            return Task.FromResult(new CompetitionCredentialImportReadResult([], []));

        var table = data.Tables[0];
        var columns = table.Columns.Cast<DataColumn>()
            .Select((column, index) => new { Name = NormalizeColumn(column.ColumnName), Index = index })
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var issues = new List<CompetitionCredentialImportIssue>();
        foreach (var required in RequiredColumns)
            if (!columns.ContainsKey(required))
                issues.Add(new(1, CompetitionCredentialImportIssueCode.MissingColumn, required));
        if (issues.Count > 0)
            return Task.FromResult(new CompetitionCredentialImportReadResult([], issues));

        var rows = new List<CompetitionCredentialImportRow>();
        var rowNumber = 1;
        foreach (DataRow row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var username = Cell(row, columns, "username");
            var email = Cell(row, columns, "email");
            var teamName = Cell(row, columns, "teamname");
            var captainText = Cell(row, columns, "captain");
            var trackKey = Cell(row, columns, "trackkey");
            var rowIssues = new List<CompetitionCredentialImportIssue>();
            if (username.Length == 0)
                rowIssues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.MissingValue, "username"));
            if (teamName.Length == 0)
                rowIssues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.MissingValue, "teamName"));
            if (!TryReadBoolean(captainText, out var captain))
                rowIssues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.InvalidBoolean, "captain"));
            if (rowIssues.Count > 0)
            {
                issues.AddRange(rowIssues);
                continue;
            }
            rows.Add(new(rowNumber, username, email.Length == 0 ? null : email, teamName, captain, trackKey.Length == 0 ? null : trackKey));
        }

        return Task.FromResult(new CompetitionCredentialImportReadResult(rows, issues));
    }

    private static string Cell(DataRow row, IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index)
            ? Convert.ToString(row[index], System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            : string.Empty;

    private static string NormalizeColumn(string value) => value.Trim().ToLowerInvariant() switch
    {
        "用户名" or "账号" or "username" or "user_name" or "login" => "username",
        "邮箱" or "电子邮箱" or "email" or "mail" => "email",
        "队伍名" or "队伍" or "team" or "teamname" or "team_name" => "teamname",
        "队长" or "captain" or "is_captain" or "iscaptain" => "captain",
        "赛道" or "赛道key" or "track" or "trackkey" or "track_key" => "trackkey",
        _ => value.Trim().ToLowerInvariant()
    };

    private static bool TryReadBoolean(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "是":
            case "队长":
                result = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "否":
            case "队员":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
