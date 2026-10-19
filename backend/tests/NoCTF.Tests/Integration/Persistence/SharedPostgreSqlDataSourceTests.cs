using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), NotInParallel]
public sealed class SharedPostgreSqlDataSourceTests
{
    [Test, Timeout(300_000)]
    public Task Scoped_and_factory_contexts_share_the_pool_and_report_each_query_once(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString(),
            ["Database:MaximumPoolSize"] = "80"
        }).Build();
        var services = new ServiceCollection().AddNoCtfDatabaseProvider(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var factory = provider.GetRequiredService<IDbContextFactory<NoCtfDbContext>>();
        await db.Database.EnsureCreatedAsync(ct);
        using var activity = new Activity("shared-datasource-test").Start();
        var traceId = activity.TraceId;
        var reads = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName && instrument.Name == "noctf.performance.stage.duration")
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            if (Activity.Current?.TraceId != traceId) return;
            var values = tags.ToArray();
            if (values.Any(tag => tag.Key == "area" && Equals(tag.Value, "Database"))
                && values.Any(tag => tag.Key == "stage" && Equals(tag.Value, "Read"))) Interlocked.Increment(ref reads);
        });
        listener.Start();
        await db.Database.OpenConnectionAsync(ct);
        var firstPid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        _ = await db.Users.AnyAsync(ct);
        await db.Database.CloseConnectionAsync();
        await using var second = await factory.CreateDbContextAsync(ct);
        await second.Database.OpenConnectionAsync(ct);
        var secondConnection = (NpgsqlConnection)second.Database.GetDbConnection();
        await Assert.That(secondConnection.ProcessID).IsEqualTo(firstPid);
        await Assert.That(new NpgsqlConnectionStringBuilder(secondConnection.ConnectionString).MaxPoolSize).IsEqualTo(80);
        _ = await second.Users.AnyAsync(ct);
        await Assert.That(reads).IsEqualTo(2);
    });
}
