using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.UseMiddleware<TenantResolutionMiddleware>();

app.Run();
