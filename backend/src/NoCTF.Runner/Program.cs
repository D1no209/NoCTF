using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNoCtfRunner(builder.Configuration);
builder.Services.AddNoCtfStandaloneRunnerPersistence(builder.Configuration);
builder.Services.AddNoCtfRunnerLogging(builder.Configuration);
var roles = HostRoles.Only(HostRole.Runner);
builder.UseWolverine(options =>
{
    options.ConfigureNoCtfPersistence(builder.Configuration, roles);
    options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
    options.ConfigureNoCtfRunnerMessaging(builder.Configuration);
});
builder.Services.AddNoCtfRoleHealthChecks(builder.Configuration, roles);

var app = builder.Build();
app.MapNoCtfHealthChecks();
app.Run();

public partial class Program;
