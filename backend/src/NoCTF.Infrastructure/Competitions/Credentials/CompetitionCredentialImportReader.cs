using System.Globalization;
using System.Text;
using System.Xml;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.Infrastructure.Competitions.Credentials;

public sealed record CompetitionCredentialImportReadResult(
    IReadOnlyList<CompetitionCredentialImportRow> Rows,
    IReadOnlyList<CompetitionCredentialImportIssue> Issues);

public interface ICompetitionCredentialImportReader
{
    Task<CompetitionCredentialImportReadResult> ReadAsync(
        Stream content, string fileName, CancellationToken cancellationToken);
}

public sealed class CompetitionCredentialImportReader : ICompetitionCredentialImportReader
{
    private static readonly string[] RequiredColumns = ["username", "teamname", "captain"];

    static CompetitionCredentialImportReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public Task<CompetitionCredentialImportReadResult> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".csv" and not ".xlsx" and not ".xls")
            return Task.FromResult(Failure(CompetitionCredentialImportIssueCode.UnsupportedFile));
        try
        {
            using var reader = extension == ".csv"
                ? ExcelReaderFactory.CreateCsvReader(content, new ExcelReaderConfiguration
                {
                    AutodetectSeparators = [',', ';', '\t'], FallbackEncoding = Encoding.UTF8, LeaveOpen = true
                })
                : ExcelReaderFactory.CreateReader(content, new ExcelReaderConfiguration { LeaveOpen = true });
            return Task.FromResult(ReadRows(reader, cancellationToken));
        }
        catch (Exception exception) when (exception is HeaderException or InvalidDataException or XmlException or DecoderFallbackException)
        {
            return Task.FromResult(Failure(CompetitionCredentialImportIssueCode.InvalidFile));
        }
    }

    private static CompetitionCredentialImportReadResult ReadRows(IExcelDataReader reader, CancellationToken ct)
    {
        if (!reader.Read()) return new([], []);
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<CompetitionCredentialImportIssue>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var column = NormalizeColumn(Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
            if (column.Length > 0 && !columns.TryAdd(column, index))
                issues.Add(new(1, CompetitionCredentialImportIssueCode.DuplicateColumn, column));
        }
        foreach (var required in RequiredColumns)
            if (!columns.ContainsKey(required))
                issues.Add(new(1, CompetitionCredentialImportIssueCode.MissingColumn, required));
        if (issues.Count > 0) return new([], issues);

        var rows = new List<CompetitionCredentialImportRow>();
        var rowNumber = 1;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;
            if (Enumerable.Range(0, reader.FieldCount).All(index => string.IsNullOrWhiteSpace(Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))))
                continue;
            var username = Cell(reader, columns, "username");
            var teamName = Cell(reader, columns, "teamname");
            var valid = true;
            if (username.Length == 0)
            {
                issues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.MissingValue, "username"));
                valid = false;
            }
            if (teamName.Length == 0)
            {
                issues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.MissingValue, "teamName"));
                valid = false;
            }
            if (!TryReadBoolean(Cell(reader, columns, "captain"), out var captain))
            {
                issues.Add(new(rowNumber, CompetitionCredentialImportIssueCode.InvalidBoolean, "captain"));
                valid = false;
            }
            if (valid)
                rows.Add(new(rowNumber, username, OptionalCell(reader, columns, "email"), teamName, captain,
                    OptionalCell(reader, columns, "trackkey"), OptionalCell(reader, columns, "fullname"),
                    OptionalCell(reader, columns, "studentnumber"), OptionalCell(reader, columns, "school")));
        }
        return new(rows, issues);
    }

    private static CompetitionCredentialImportReadResult Failure(CompetitionCredentialImportIssueCode code) =>
        new([], [new(0, code, string.Empty)]);

    private static string Cell(IExcelDataReader reader, IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index)
            ? Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty : string.Empty;

    private static string? OptionalCell(IExcelDataReader reader, IReadOnlyDictionary<string, int> columns, string name) =>
        Cell(reader, columns, name) is { Length: > 0 } value ? value : null;

    private static string NormalizeColumn(string value) => value.Trim().ToLowerInvariant() switch
    {
        "用户名" or "账号" or "username" or "user_name" or "login" => "username",
        "邮箱" or "电子邮箱" or "email" or "mail" => "email",
        "队伍名称" or "队伍名" or "队伍" or "team" or "teamname" or "team_name" => "teamname",
        "是否队长" or "队长" or "captain" or "is_captain" or "iscaptain" => "captain",
        "赛道" or "赛道key" or "track" or "trackkey" or "track_key" => "trackkey",
        "姓名" or "真实姓名" or "fullname" or "full_name" or "name" => "fullname",
        "学号" or "studentnumber" or "student_number" or "studentid" or "student_id" => "studentnumber",
        "学校" or "院校" or "school" or "university" => "school",
        _ => value.Trim().ToLowerInvariant()
    };

    private static bool TryReadBoolean(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "y": case "是": case "队长":
                result = true;
                return true;
            case "false": case "0": case "no": case "n": case "否": case "队员":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
