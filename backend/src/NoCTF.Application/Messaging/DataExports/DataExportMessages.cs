namespace NoCTF.Application.Messaging;

public sealed record GenerateDataExport(Guid DataExportId);

public sealed record ExpireDataExport(Guid DataExportId);

public sealed record PurgeDataExport(Guid DataExportId);
