using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Infrastructure;
using FastEndpoints.Swagger;

var builder = WebApplication.CreateBuilder(args);
var exportSwagger = args.Contains("--export-swagger-docs", StringComparer.OrdinalIgnoreCase);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
// Swagger export uses the production composition so every endpoint has the same
// dependency graph as a running API; no endpoint is silently omitted behind fakes.
builder.Services.AddNoCtfApi(builder.Configuration, true);
builder.Services.AddNoCtfAuthentication(builder.Configuration);

var app = builder.Build();
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.MigrateNoCtfAsync();
    await app.StartAsync();
    await app.StopAsync();
    return;
}
app.UseNoCtfPipeline();
app.UseNoCtfEndpoints();
if (exportSwagger)
{
    await app.ExportSwaggerDocsAndExitAsync(["v1"]);
    return;
}
app.MapHub<CompetitionHub>("/hubs/competition");
app.Run();

public partial class Program;
