using System.IO.Compression;
using System.Text;
using NoCTF.Application.Competitions.Credentials;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NoCTF.Infrastructure.Competitions.Credentials;

public sealed record CompetitionCredentialExport(string FileName, string ContentType, MemoryStream Content);

public sealed class CompetitionCredentialExportService
{
    static CompetitionCredentialExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Drawing.FontManager.RegisterFontFromEmbeddedResource(
            typeof(CompetitionCredentialExportService).Assembly, "NoCTF.Credentials.SarasaUiSC-Regular.ttf");
    }

    public CompetitionCredentialExport CreateZip(CompetitionCredentialBatchView batch)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "credentials.csv", Csv(batch));
            WriteEntry(archive, "credentials.html", Html(batch));
            var pdf = CreatePdf(batch);
            var pdfEntry = archive.CreateEntry("credentials.pdf", CompressionLevel.Fastest);
            using var target = pdfEntry.Open();
            pdf.Position = 0;
            pdf.CopyTo(target);
            pdf.Dispose();
        }
        output.Position = 0;
        return new($"noctf-credentials-{batch.BatchId:N}.zip", "application/zip", output);
    }

    public MemoryStream CreatePdf(CompetitionCredentialBatchView batch)
    {
        var output = new MemoryStream();
        Document.Create(document => document.Page(page =>
        {
            page.Margin(28);
            page.Size(PageSizes.A4.Landscape());
            page.DefaultTextStyle(style => style.FontFamily("Sarasa UI SC").FontSize(9));
            page.Header().Text($"NoCTF 账号凭据 · {batch.BatchId:N}").SemiBold().FontSize(16);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(0.5f);
                    columns.RelativeColumn(1.9f);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(0.8f);
                });
                table.Header(header =>
                {
                    foreach (var title in new[] { "用户名", "邮箱", "队伍", "队长", "密码", "姓名", "学号", "学校", "赛道" })
                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(title).SemiBold();
                });
                foreach (var row in batch.Rows)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.UserName);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.Email ?? string.Empty);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.TeamName);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.Captain ? "是" : "否");
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.Password);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.FullName ?? string.Empty);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.StudentNumber ?? string.Empty);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.School ?? string.Empty);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(row.TrackKey);
                }
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("批次：").SemiBold();
                text.Span(batch.BatchId.ToString("N"));
            });
        })).GeneratePdf(output);
        output.Position = 0;
        return output;
    }

    private static string Csv(CompetitionCredentialBatchView batch)
    {
        var builder = new StringBuilder();
        builder.AppendLine("username,email,teamName,captain,trackKey,password,fullName,studentNumber,school");
        foreach (var row in batch.Rows)
            builder.AppendLine(string.Join(',',
                Escape(row.UserName, protectFormula: false), Escape(row.Email), Escape(row.TeamName),
                row.Captain ? "true" : "false", Escape(row.TrackKey), Escape(row.Password, protectFormula: false),
                Escape(row.FullName), Escape(row.StudentNumber), Escape(row.School)));
        return builder.ToString();
    }

    private static string Html(CompetitionCredentialBatchView batch)
    {
        var builder = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>NoCTF credentials</title><style>body{font-family:Arial,sans-serif}table{border-collapse:collapse;width:100%}th,td{border:1px solid #888;padding:6px;text-align:left;overflow-wrap:anywhere} @media print{button{display:none}}</style></head><body><button onclick=\"window.print()\">打印</button><table><thead><tr><th>用户名</th><th>邮箱</th><th>队伍</th><th>队长</th><th>赛道</th><th>密码</th><th>姓名</th><th>学号</th><th>学校</th></tr></thead><tbody>");
        foreach (var row in batch.Rows)
            builder.Append("<tr><td>").Append(HtmlEncode(row.UserName)).Append("</td><td>").Append(HtmlEncode(row.Email ?? string.Empty)).Append("</td><td>").Append(HtmlEncode(row.TeamName)).Append("</td><td>").Append(row.Captain ? "是" : "否").Append("</td><td>").Append(HtmlEncode(row.TrackKey)).Append("</td><td>").Append(HtmlEncode(row.Password)).Append("</td><td>").Append(HtmlEncode(row.FullName ?? string.Empty)).Append("</td><td>").Append(HtmlEncode(row.StudentNumber ?? string.Empty)).Append("</td><td>").Append(HtmlEncode(row.School ?? string.Empty)).Append("</td></tr>");
        return builder.Append("</tbody></table></body></html>").ToString();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(name.EndsWith(".csv", StringComparison.Ordinal)));
        writer.Write(content);
    }

    private static string Escape(string? value, bool protectFormula = true)
    {
        var text = value ?? string.Empty;
        if (protectFormula && text.TrimStart() is { Length: > 0 } trimmed && trimmed[0] is '=' or '+' or '-' or '@')
            text = "'" + text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
