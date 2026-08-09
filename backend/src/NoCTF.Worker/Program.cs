using NoCTF.Hosting;
using NoCTF.Infrastructure;
using NoCTF.Worker;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNoCtfInfrastructure(builder.Configuration);
builder.Services.AddNoCtfWorkerRole();
builder.Services.AddNoCtfWorkerLogging(builder.Configuration);
var roles = HostRoles.Only(HostRole.Worker);
builder.UseWolverine(options =>
{
    options.ConfigureNoCtfPersistence(builder.Configuration, roles);
    options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
    options.ConfigureNoCtfWorkerMessaging();
});

await builder.Build().RunAsync();
