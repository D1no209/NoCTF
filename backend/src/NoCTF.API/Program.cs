using System.Text;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SpaServices.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using NoCTF.API;
using NoCTF.API.Auth;
using NoCTF.API.Logging;
using NoCTF.API.Permissions;
using NoCTF.API.Plugins;
using NoCTF.API.SignalR;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application;
using NoCTF.Application.Events;
using NoCTF.Application.Leaderboard;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// CORS — must be before SignalR so the policy is available
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // required for SignalR WebSocket/SSE
    });
});

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

        // Allow JWT from query string for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs/leaderboard") ||
                     path.StartsWithSegments("/hubs/game") ||
                     path.StartsWithSegments("/hubs/monitor")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// SignalR with Redis backplane
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConnection, options =>
    {
        options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel(
            "NoCTF", StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });

// Hub notifier service (singleton — IHubContext is thread-safe)
builder.Services.AddSingleton<IHubNotifierService, HubNotifierService>();

// Log buffer (singleton) + custom logger provider
builder.Services.AddSingleton<LogBuffer>();
builder.Services.AddSingleton<ILoggerProvider, LogStreamerLoggerProvider>();

// Redis IConnectionMultiplexer (shared instance for leaderboard cache)
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));

// Leaderboard services
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddSingleton<IRedisLeaderboardCache, RedisLeaderboardCache>();
builder.Services.AddScoped<ISubmissionEventHandler, LeaderboardSyncHandler>();

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

// Container manager (Docker provider)
builder.Services.AddSingleton(
    _ => new NoCTF.Container.Docker.DockerProvider(builder.Configuration["Docker:Host"]));
builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();

// Cold-load plugins from plugins/ directory
PluginLoader.LoadAndRegisterAll(builder.Services, builder.Configuration);

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgresql")
    .AddCheck<RedisHealthCheck>("redis")
    .AddCheck<DockerHealthCheck>("docker");

var app = builder.Build();

// Seed default admin
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DataSeeder.SeedAsync(db);
}
catch
{
    // Ignore seed errors during startup (migrations may not be applied yet)
}

// Wire hub context into LogBuffer so it can broadcast log entries via SignalR
var logBuffer = app.Services.GetRequiredService<LogBuffer>();
var monitorHubContext = app.Services.GetRequiredService<IHubContext<MonitorHub, IMonitorClient>>();
logBuffer.SetHubContext(monitorHubContext);

var localBasePath = builder.Configuration["StorageProvider:Local:BasePath"] ?? "uploads";
if (!Directory.Exists(localBasePath))
    Directory.CreateDirectory(localBasePath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(localBasePath)),
    RequestPath = "/api/files"
});

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = null;
    c.Endpoints.Configurator = ep =>
    {
        // Only add audit post-processor to endpoints implementing IAuditableEndpoint
        var epType = ep.GetType().GetProperty("EndpointType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(ep) as Type;
        if (epType is not null && typeof(IAuditableEndpoint).IsAssignableFrom(epType))
        {
            ep.PostProcessors(FastEndpoints.Order.After, new AuditLogPostProcessor());
        }
    };
});
app.UseSwaggerGen();

app.MapGet("/api/health", async (HealthCheckService healthCheckService) =>
{
    var report = await healthCheckService.CheckHealthAsync();
    var result = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    };
    var statusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
    return Results.Json(result, statusCode: statusCode);
}).AllowAnonymous();

app.UseMiddleware<TenantResolutionMiddleware>();

// SignalR hub endpoints
app.MapHub<LeaderboardHub>("/hubs/leaderboard");
app.MapHub<GameHub>("/hubs/game");
app.MapHub<MonitorHub>("/hubs/monitor");

if (app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "../../frontend";
        spa.Options.DevServerPort = 5173;
    });
}

app.Run();
