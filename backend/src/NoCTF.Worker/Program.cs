using NoCTF.Infrastructure;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using NoCTF.Application.Runtime.Instances;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNoCtfInfrastructure(builder.Configuration);
builder.UseWolverine(options =>
{
    var postgres = builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
    options.PersistMessagesWithPostgresql(postgres, "wolverine");
    options.UseEntityFrameworkCoreTransactions();
    options.Durability.Mode = DurabilityMode.Balanced;
    options.ListenToPostgresqlQueue("noctf-worker").UseDurableInbox();
    var runnerPool = builder.Configuration["Runner:Pool"] ?? "default";
    var runnerQueue = RunnerQueueName.FromPool(runnerPool);
    options.PublishMessage<ProvisionContainerRuntime>().ToPostgresqlQueue(runnerQueue.Value);
    options.PublishMessage<StopContainerRuntime>().ToPostgresqlQueue(runnerQueue.Value);
});

await builder.Build().RunAsync();
