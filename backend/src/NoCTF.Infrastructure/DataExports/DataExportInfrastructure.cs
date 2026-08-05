using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.DataExports;

namespace NoCTF.Infrastructure.DataExports;

internal static class DataExportInfrastructure
{
    internal static IServiceCollection AddNoCtfDataExports(
        this IServiceCollection services)
    {
        services.AddScoped<IDataExportStore, DataExportStore>();
        services.AddScoped<IDataExportProcessor, DataExportProcessor>();
        services.AddScoped<RequestDataExport>();
        services.AddScoped<ListDataExports>();
        services.AddScoped<AccessDataExport>();
        services.AddScoped<ProcessDataExport>();
        return services;
    }
}
