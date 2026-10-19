using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Persistence;
using Npgsql;

namespace NoCTF.Persistence.PostgreSql;

public static class PostgreSqlPersistence
{
    public static IServiceCollection AddNoCtfDatabaseProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "PostgreSql";
        if (!string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Database provider '{provider}' is not installed. This build supports PostgreSql only.");
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddSingleton<NpgsqlDataSource>(_ =>
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString);
            settings.MaxPoolSize = Math.Min(settings.MaxPoolSize,
                configuration.GetValue("Database:MaximumPoolSize", 80));
            settings.MaxAutoPrepare = configuration.GetValue("Database:MaximumAutoPreparedStatements", 64);
            settings.AutoPrepareMinUsages = configuration.GetValue("Database:AutoPrepareMinUsages", 2);
            var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString) { Name = "NoCTF" };
            return builder.Build();
        });
        void Configure(IServiceProvider provider, DbContextOptionsBuilder options) => options
                .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql
                    .MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)
                    // Aggregate decisions keep one statement's snapshot; bounded read projections opt into splitting.
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new NoCTF.Infrastructure.Observability.DatabaseCommandPerformanceInterceptor(),
                    new NoCTF.Infrastructure.Observability.DatabaseConnectionPerformanceInterceptor());
        services.AddScoped<NoCtfDbContext>();
        services.AddDbContextFactory<NoCtfDbContext>(Configure);
        return services;
    }
}

public sealed class PostgreSqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<NoCtfDbContext>
{
    public NoCtfDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=noctf;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new(options);
    }
}
