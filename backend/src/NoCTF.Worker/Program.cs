using Microsoft.AspNetCore.Builder;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Hosting.Observability;
using NoCTF.Infrastructure;
using NoCTF.Worker;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNoCtfInfrastructure(builder.Configuration);
builder.Services.AddNoCtfWorkerRole(builder.Configuration);
builder.Services.AddNoCtfWorkerLogging(builder.Configuration);
var roles = HostRoles.Only(HostRole.Worker);
builder.UseWolverine(options =>
{
    options.ConfigureNoCtfPersistence(builder.Configuration, roles);
    options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
    options.ConfigureNoCtfWorkerMessaging(builder.Configuration);
});
builder.Services.AddNoCtfRoleHealthChecks(builder.Configuration, roles);
builder.Services.AddNoCtfObservability(builder.Configuration, "noctf-worker");

var app = builder.Build();
app.UseNoCtfObservability();
app.MapNoCtfHealthChecks();
await app.RunAsync();
