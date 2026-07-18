using NoCTF.Runner.Composition;
using FastEndpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNoCtfRunner(builder.Configuration);
var app = builder.Build();
app.UseRunnerSecurity(builder.Configuration);
app.UseFastEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program;
