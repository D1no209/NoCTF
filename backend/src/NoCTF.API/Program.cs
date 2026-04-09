using System.Text;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using NoCTF.API;
using NoCTF.API.Auth;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

var builder = WebApplication.CreateBuilder(args);

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSettings.GetValue<string>("Secret")!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.GetValue<string>("Issuer") ?? "NoCTF",
            ValidAudience = jwtSettings.GetValue<string>("Audience") ?? "NoCTF",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o =>
{
    o.DocumentSettings = s =>
    {
        s.Title = "NoCTF API";
        s.Version = "v1";
        s.AddAuth("Bearer", new NSwag.OpenApiSecurityScheme
        {
            Type = NSwag.OpenApiSecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter JWT Bearer token"
        });
    };
});

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IStorageProvider>(StorageProviderFactory.Create(builder.Configuration));

// JWT token service
builder.Services.AddScoped<JwtTokenService>();

// Permission services
builder.Services.AddScoped<ICompetitionPermissionService, CompetitionPermissionService>();
builder.Services.AddScoped<ITeamPermissionService, TeamPermissionService>();

var app = builder.Build();

var localBasePath = builder.Configuration["StorageProvider:Local:BasePath"] ?? "uploads";
if (!Directory.Exists(localBasePath))
    Directory.CreateDirectory(localBasePath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(localBasePath)),
    RequestPath = "/api/files"
});

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = null;
});
app.UseSwaggerGen();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.UseMiddleware<TenantResolutionMiddleware>();

app.Run();
