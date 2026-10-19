using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Nats;
using Npgsql;
using ZiggyCreatures.Caching.Fusion;

if (args.Length != 2) throw new ArgumentException("Expected private configuration file and output directory.");
var input = JsonSerializer.Deserialize<DatabaseInput>(await File.ReadAllTextAsync(args[0]))
    ?? throw new InvalidDataException("Missing database inspection input.");
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:Redis"] = input.Redis }).Build();
await using var services = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
await using var nats = new NatsConnection(new NatsOpts { Url = input.Nats });
var settings = new NpgsqlConnectionStringBuilder(input.PostgreSql) { MaxPoolSize = 1, Options = "-c default_transaction_read_only=on" };
await using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
var inspector = new ProjectionExplainInterceptor(output);
var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(dataSource)
    .UseSnakeCaseNamingConvention().AddInterceptors(inspector).Options;
await using var db = new NoCtfDbContext(options);
var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
    new NatsLeaderboardRefreshPublisher(nats), services.GetRequiredService<IFusionCacheProvider>());
var published = await new NatsLeaderboardPublicationFence(nats, services.GetRequiredService<IFusionCacheProvider>())
    .GetAsync(input.CompetitionId, timeout.Token);
var teamCount = 0;
int? projectedPayloadCharacters = null;
if (input.Admission)
{
    using var messaging = Host.CreateDefaultBuilder().UseWolverine(options =>
    {
        options.Discovery.DisableConventionalDiscovery();
        options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
        options.UseNats(input.Nats).AutoProvision();
    }).Build();
    await messaging.StartAsync(timeout.Token);
    var user = await db.Teams.Where(team => team.CompetitionId == input.CompetitionId).Select(team => team.CaptainId).FirstAsync(timeout.Token);
    var challenge = await db.CompetitionChallenges.Where(item => item.CompetitionId == input.CompetitionId).Select(item => item.Id).FirstAsync(timeout.Token);
    var intake = new GameplayFactIntakeStore(db, new WolverinePostCommitMessagePublisher(messaging.Services.GetRequiredService<IMessageBus>(), db));
    _ = await intake.LoadAdmissionAsync(input.CompetitionId, challenge, user, timeout.Token)
        ?? throw new InvalidOperationException("Admission scope was not found.");
    await messaging.StopAsync(timeout.Token);
}
else
{
    var projection = await cache.CreateScoreboardAsync(input.CompetitionId, DateTimeOffset.UtcNow, timeout.Token)
        ?? throw new InvalidOperationException("Competition projection was not found.");
    teamCount = projection.Snapshot.Teams.Count;
    projectedPayloadCharacters = JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
}
var file = input.Admission ? "admission-plans.json" : "projection-plans.json";
await File.WriteAllTextAsync(Path.Combine(output, file), JsonSerializer.Serialize(new
{
    utc = DateTimeOffset.UtcNow, competitionId = input.CompetitionId,
    publishedPayloadCharacters = published?.Payload.Length,
    projectedPayloadCharacters,
    operation = input.Admission ? "admission" : "projection", teams = teamCount, commands = inspector.Plans,
    note = "Offline EXPLAIN ANALYZE runs each read before its production EF query; timings are diagnostic and excluded from load-test results."
}, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
Console.WriteLine($"Read plans captured: {inspector.Plans.Count}; teams: {teamCount}.");

internal sealed record DatabaseInput(string PostgreSql, string Redis, string Nats, Guid CompetitionId, bool Admission = false);
internal sealed record ProjectionPlan(int Sequence, string QuerySha256, string File, double PlanningMs, double ExecutionMs);

internal sealed class ProjectionExplainInterceptor(string output) : DbCommandInterceptor
{
    public List<ProjectionPlan> Plans { get; } = [];

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        if (eventData.CommandSource != CommandSource.LinqQuery)
            throw new InvalidOperationException("Projection inspection only permits EF read queries.");
        var connection = (NpgsqlConnection)(command.Connection ?? throw new InvalidOperationException("Missing connection."));
        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.CommandText,
            connection, eventData.Context?.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        foreach (NpgsqlParameter parameter in command.Parameters) explain.Parameters.Add(parameter.Clone());
        var plan = (string)(await explain.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Missing execution plan."));
        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement[0];
        var sequence = Plans.Count + 1;
        var file = $"plan-{sequence:D4}.json";
        await File.WriteAllTextAsync(Path.Combine(output, file), plan, ct);
        Plans.Add(new(sequence, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText))), file,
            root.GetProperty("Planning Time").GetDouble(), root.GetProperty("Execution Time").GetDouble()));
        return result;
    }
}
