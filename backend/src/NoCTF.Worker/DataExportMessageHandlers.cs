using NoCTF.Application.DataExports;
using NoCTF.Application.Messaging;

namespace NoCTF.Worker;

public static class DataExportMessageHandlers
{
    public static Task Handle(
        GenerateDataExport message,
        ProcessDataExport process,
        CancellationToken cancellationToken) =>
        process.GenerateAsync(message.DataExportId, cancellationToken);

    public static Task Handle(
        ExpireDataExport message,
        ProcessDataExport process,
        CancellationToken cancellationToken) =>
        process.ExpireAsync(message.DataExportId, cancellationToken);

    public static Task Handle(
        PurgeDataExport message,
        ProcessDataExport process,
        CancellationToken cancellationToken) =>
        process.PurgeAsync(message.DataExportId, cancellationToken);
}
