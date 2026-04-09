using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using NoCTF.API;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IStorageProvider>(StorageProviderFactory.Create(builder.Configuration));

var app = builder.Build();

var localBasePath = builder.Configuration["StorageProvider:Local:BasePath"] ?? "uploads";
if (!Directory.Exists(localBasePath))
    Directory.CreateDirectory(localBasePath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(localBasePath)),
    RequestPath = "/api/files"
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.UseMiddleware<TenantResolutionMiddleware>();

app.Run();
