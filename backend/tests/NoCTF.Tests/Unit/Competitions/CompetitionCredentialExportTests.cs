using System.IO.Compression;
using ExcelDataReader;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Credentials;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class CompetitionCredentialExportTests
{
    [Test]
    public async Task Csv_preserves_login_credentials_exactly_while_protecting_unrestricted_metadata()
    {
        var row = new CompetitionCredentialDownloadRow(Guid.NewGuid(), "-participant", null, "=1+1", true,
            "default", "@HistoricalPassword", "姓名", "001234", "学校");
        var batch = new CompetitionCredentialBatchView(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "participants.csv", 1, [row]);
        using var exported = new CompetitionCredentialExportService().CreateZip(batch).Content;
        using var archive = new ZipArchive(exported);
        await using var csv = new MemoryStream();
        await using (var entry = archive.GetEntry("credentials.csv")!.Open()) await entry.CopyToAsync(csv);
        csv.Position = 0;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateCsvReader(csv);
        await Assert.That(reader.Read()).IsTrue();
        await Assert.That(reader.Read()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(row.UserName);
        await Assert.That(reader.GetString(5)).IsEqualTo(row.Password);
        await Assert.That(reader.GetString(2)).IsEqualTo("'=1+1");
        await Assert.That(reader.GetString(7)).IsEqualTo("001234");
    }
}
