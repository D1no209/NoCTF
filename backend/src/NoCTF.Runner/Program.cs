using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Libvirt;
using NoCTF.Application.Runtime.Instances;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNoCtfRunner(builder.Configuration);
var postgres = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
    options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
var runnerPool = builder.Configuration["Runner:Pool"] ?? "default";
var queueName = RunnerQueueName.FromPool(runnerPool);
builder.UseWolverine(options =>
{
    options.PersistMessagesWithPostgresql(postgres, "wolverine");
    options.UseEntityFrameworkCoreTransactions();
    options.Durability.Mode = DurabilityMode.Balanced;
    options.ListenToPostgresqlQueue(queueName.Value).UseDurableInbox();
});
var app = builder.Build();
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => TypedResults.Ok(new { status = "ready", runnerPool }));
app.Run();

public partial class Program;
