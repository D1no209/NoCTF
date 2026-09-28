using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Persistence.Sqlite;

public static class SqliteDevelopmentPersistence
{
    public static IServiceCollection AddNoCtfSqliteDevelopmentDatabase(
        this IServiceCollection services,
        string databaseName)
    {
        var safeName = string.Concat(databaseName.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        if (safeName.Length == 0)
            throw new InvalidOperationException("Development database name is invalid.");
        var connectionString = $"Data Source={safeName};Mode=Memory;Cache=Shared";
        var anchor = new SqliteConnection(connectionString);
        anchor.Open();
        services.AddSingleton(anchor);
        void Configure(DbContextOptionsBuilder options) => options.UseSqlite(connectionString);
        services.AddDbContext<NoCtfDbContext>(Configure,
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<NoCtfDbContext>(Configure);
        return services;
    }
}
