using System.Text;
using System.Security.Claims;
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
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Security;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// CORS — must be before SignalR so the policy is available
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://127.0.0.1:5173",
                  "http://localhost:4173",
                  "http://127.0.0.1:4173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // required for SignalR WebSocket/SSE
    });
});

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSettings.GetValue<string>("Secret")!;
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException("JwtSettings:Secret must be configured and at least 32 characters long.");
}

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
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

builder.Services.AddNoCtfApplicationCore();

// Leaderboard services
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<ILeaderboardProjectionBuilder>(sp => (LeaderboardService)sp.GetRequiredService<ILeaderboardService>());
builder.Services.AddScoped<ILeaderboardInsightService, LeaderboardInsightService>();
builder.Services.AddSingleton<IRedisLeaderboardCache, RedisLeaderboardCache>();
builder.Services.AddScoped<ISubmissionEventHandler, LeaderboardSyncHandler>();
builder.Services.AddScoped<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddScoped<IPatchArchiveValidator, PatchArchiveValidator>();

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var autoMigrate = builder.Configuration.GetValue("Database:AutoMigrate", !app.Environment.IsDevelopment());

    if (autoMigrate)
    {
        await db.Database.MigrateAsync();
    }

    await DataSeeder.SeedAsync(db, builder.Configuration);
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

if (!app.Environment.IsDevelopment())
{
    var spaFileProvider = new PhysicalFileProvider(Path.Combine(Environment.CurrentDirectory, "wwwroot"));
    app.UseSpaStaticFiles(new StaticFileOptions { FileProvider = spaFileProvider });
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

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

// SignalR hub endpoints
app.MapHub<LeaderboardHub>("/hubs/leaderboard");
app.MapHub<GameHub>("/hubs/game");
app.MapHub<MonitorHub>("/hubs/monitor");

if (app.Environment.IsDevelopment())
{
    app.UseWhen(
        context =>
            !context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.StartsWithSegments("/hubs") &&
            !context.Request.Path.StartsWithSegments("/swagger"),
        spaApp => spaApp.UseSpa(spa =>
    {
        spa.UseProxyToSpaDevelopmentServer("http://localhost:5173");
    }));
}
else
{
    var spaFileProvider = new PhysicalFileProvider(Path.Combine(Environment.CurrentDirectory, "wwwroot"));
    app.UseSpa(spa =>
    {
        spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
        {
            FileProvider = spaFileProvider,
            OnPrepareResponse = fileCtx =>
            {
                fileCtx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            }
        };
    });
}

app.Run();
