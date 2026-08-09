using NoCTF.Hosting;
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

var app = builder.Build();
var runnerPool = builder.Configuration["Runner:Pool"] ?? "default";
var runnerId = builder.Configuration["Runner:Id"]
    ?? throw new InvalidOperationException("Runner:Id is required.");
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => TypedResults.Ok(new
{
    status = "ready",
    runnerPool,
    runnerId
}));
app.Run();

public partial class Program;
