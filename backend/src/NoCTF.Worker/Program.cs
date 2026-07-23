using NoCTF.Infrastructure;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Messaging;
using Wolverine.ErrorHandling;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNoCtfInfrastructure(builder.Configuration);
builder.UseWolverine(options =>
{
    var postgres = builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
    options.PersistMessagesWithPostgresql(postgres, "wolverine");
    options.UseEntityFrameworkCoreTransactions();
    options.Durability.Mode = DurabilityMode.Balanced;
    options.Policies.OnException<TimeoutException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<System.Net.Http.HttpRequestException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<Npgsql.NpgsqlException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>()
        .RetryTimes(5);
    options.ListenToPostgresqlQueue("noctf-worker").UseDurableInbox();
});

await builder.Build().RunAsync();
