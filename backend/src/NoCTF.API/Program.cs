using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Net;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Events;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Application.Security;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Storage;
using NoCTF.PluginBase;
using NoCTF.Runner.Client;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var usesLocalStorage = StorageProviderFactory.UsesLocalStorage(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = RequestBodyLimits.MaximumMultipartBodyLength(builder.Configuration);
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
            options.KnownProxies.Add(address);
    }
    if (builder.Configuration.GetValue("ForwardedHeaders:TrustAll", false))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

// CORS — must be before SignalR so the policy is available
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var origins = configuredOrigins
            .Select(NormalizeCorsOrigin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (builder.Environment.IsDevelopment() && origins.Length == 0)
        {
            origins =
            [
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:4173",
                "http://127.0.0.1:4173"
            ];
        }
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSettings.GetValue<string>("Secret")!;
if (builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
        throw new InvalidOperationException("JwtSettings:Secret must be configured and at least 32 characters long.");
}
else
{
    SecretValueValidator.RequireSafe("JwtSettings:Secret", jwtSecret, 32);
    var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
    if (SecretValueValidator.IsConnectionStringUnsafe(defaultConnection))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection contains a missing, weak, or placeholder value.");
    var configuredSeedPassword = builder.Configuration["SeedAdmin:Password"];
    if (!string.IsNullOrWhiteSpace(configuredSeedPassword))
        SecretValueValidator.RequireSafe("SeedAdmin:Password", configuredSeedPassword, 12);
    StorageProviderFactory.ValidateLocalUrlSigningKey(builder.Configuration);
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
            ClockSkew = TimeSpan.FromSeconds(30),
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
            },
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? context.Principal?.FindFirst("sub")?.Value;
                if (!Guid.TryParse(userIdClaim, out var userId))
                {
                    context.Fail("Invalid user token.");
                    return;
                }

                var tokenVersionText = context.Principal?.FindFirst("token_version")?.Value;
                if (!int.TryParse(tokenVersionText, out var tokenVersion))
                {
                    context.Fail("Token version missing.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var tokenVersionCache = context.HttpContext.RequestServices
                    .GetRequiredService<IUserTokenVersionCache>();
                var currentVersion = await tokenVersionCache.GetAsync(
                    userId,
                    db,
                    context.HttpContext.RequestAborted);
                if (currentVersion is null || currentVersion.Value != tokenVersion)
                    context.Fail("Token has been revoked.");
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth-register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth-refresh", httpContext =>
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var partition = $"{userId}:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("competition-submit", httpContext =>
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var partition = $"{userId ?? "anonymous"}:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("public-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("public-stream", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 3,
                QueueLimit = 0
            }));
    options.AddPolicy("hub-connect", httpContext =>
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var partition = $"{userId}:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetConcurrencyLimiter(
            partition,
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 10,
                QueueLimit = 0
            });
    });
    options.AddPolicy("health-read", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 4,
                QueueLimit = 0
            }));
});

// SignalR with Redis backplane
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(redisConnection))
    throw new InvalidOperationException("ConnectionStrings:Redis must be configured outside Development.");
redisConnection ??= "localhost:6379";
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConnection, options =>
    {
        options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel(
            "NoCTF", StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });

// Direct API notifications use SignalR. Worker notifications are persisted to
// a Redis Stream and relayed by one API replica from the shared consumer group.
builder.Services.AddSingleton<HubNotifierService>();
builder.Services.AddSingleton<IHubNotifierService>(sp => sp.GetRequiredService<HubNotifierService>());
builder.Services.AddSingleton<IHubNotificationRelayTarget>(sp => sp.GetRequiredService<HubNotifierService>());
builder.Services.AddHostedService<RedisHubNotificationRelay>();

// Log buffer (singleton) + custom logger provider
builder.Services.AddSingleton<LogBuffer>();
builder.Services.AddSingleton<ILoggerProvider, LogStreamerLoggerProvider>();

// Redis IConnectionMultiplexer (shared instance for leaderboard cache)
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = builder.Environment.IsProduction();
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<IUserTokenVersionCache, RedisUserTokenVersionCache>();

builder.Services.AddNoCtfApplicationCore();

// Leaderboard services
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<ILeaderboardProjectionBuilder>(sp => (LeaderboardService)sp.GetRequiredService<ILeaderboardService>());
builder.Services.AddScoped<ILeaderboardInsightService, LeaderboardInsightService>();
builder.Services.AddSingleton<IRedisLeaderboardCache, RedisLeaderboardCache>();
builder.Services.AddScoped<ISubmissionEventHandler, LeaderboardSyncHandler>();
builder.Services.AddScoped<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddScoped<IPatchArchiveValidator, PatchArchiveValidator>();
builder.Services.AddSingleton<NoCTF.API.Endpoints.Competitions.AwdpScreenSnapshotCache>();

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

builder.Services.AddSingleton<IStorageProvider>(_ => StorageProviderFactory.Create(builder.Configuration));

// JWT token service
builder.Services.AddScoped<JwtTokenService>();

// Permission services
builder.Services.AddScoped<ICompetitionPermissionService, CompetitionPermissionService>();
builder.Services.AddScoped<ITeamPermissionService, TeamPermissionService>();

// Container manager. Production deployments should use the Runner boundary; direct Docker remains a local fallback.
var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(runnerBaseUrl))
    throw new InvalidOperationException("Runner:BaseUrl must be configured outside Development.");
Uri? runnerBaseUri = null;
if (!string.IsNullOrWhiteSpace(runnerBaseUrl) &&
    (!Uri.TryCreate(runnerBaseUrl, UriKind.Absolute, out runnerBaseUri) ||
     (runnerBaseUri.Scheme != Uri.UriSchemeHttp && runnerBaseUri.Scheme != Uri.UriSchemeHttps) ||
     !string.IsNullOrEmpty(runnerBaseUri.UserInfo)))
{
    throw new InvalidOperationException("Runner:BaseUrl must be an absolute HTTP(S) URL without embedded credentials.");
}
if (!string.IsNullOrWhiteSpace(runnerBaseUrl))
{
    if (!builder.Environment.IsDevelopment())
        SecretValueValidator.RequireSafe("Runner:ApiKey", builder.Configuration["Runner:ApiKey"], 24);
    builder.Services.AddHttpClient<IRunnerClient, HttpRunnerClient>(client =>
    {
        client.BaseAddress = runnerBaseUri;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(
            builder.Configuration.GetValue("Runner:TimeoutSeconds", 900), 30, 3600));
        var runnerApiKey = builder.Configuration["Runner:ApiKey"];
        if (!string.IsNullOrWhiteSpace(runnerApiKey))
            client.DefaultRequestHeaders.Add("X-Runner-Token", runnerApiKey);
    });
    builder.Services.AddScoped<IContainerManager, RunnerBackedContainerManager>();
}
else
{
    builder.Services.AddSingleton(
        _ => new NoCTF.Container.Docker.DockerProvider(builder.Configuration["Docker:Host"]));
    builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();
}

// Cold-load plugins from plugins/ directory
PluginLoader.LoadAndRegisterAll(builder.Services, builder.Configuration, PluginHostRole.Api);

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgresql")
    .AddCheck<RedisHealthCheck>("redis")
    .AddCheck<DockerHealthCheck>("docker");

var app = builder.Build();
var migrateOnly = args.Any(arg => string.Equals(arg, "--migrate-only", StringComparison.OrdinalIgnoreCase));

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    var autoMigrate = builder.Configuration.GetValue("Database:AutoMigrate", false);

    if (migrateOnly || autoMigrate)
    {
        await db.Database.MigrateAsync();
    }

    if (migrateOnly || autoMigrate || app.Environment.IsDevelopment())
    {
        await DataSeeder.SeedAsync(
            db,
            builder.Configuration,
            allowDefaultAdminCredentials: app.Environment.IsDevelopment());
    }

    if (!migrateOnly)
    {
        // Materialize every plugin-owned registry before accepting traffic.
        // Invalid duplicate keys or overlapping score-rebuild ownership must
        // fail deterministically, not on the first request that uses it.
        _ = services.GetRequiredService<ICompetitionModeRegistry>();
        _ = services.GetRequiredService<ICompetitionFileActionRegistry>();
        _ = services.GetRequiredService<IChallengeSubmissionHandlerRegistry>();
        _ = services.GetRequiredService<IChallengeFeatureRegistry>();
        _ = services.GetRequiredService<IChallengeAdminFeatureRegistry>();
        _ = services.GetRequiredService<ICompetitionJobRegistry>().Jobs;
        _ = services.GetRequiredService<IScoreSignalEmitter>();
        _ = services.GetRequiredService<ICtfScoreRebuilder>();
    }
}

if (migrateOnly)
    return;

// Wire hub context into LogBuffer so it can broadcast log entries via SignalR
var logBuffer = app.Services.GetRequiredService<LogBuffer>();
var monitorHubContext = app.Services.GetRequiredService<IHubContext<MonitorHub, IMonitorClient>>();
logBuffer.SetHubContext(monitorHubContext);

var localBasePath = builder.Configuration["StorageProvider:Local:BasePath"] ?? "uploads";
if (usesLocalStorage && !Directory.Exists(localBasePath))
    Directory.CreateDirectory(localBasePath);

if (!app.Environment.IsDevelopment())
{
    var spaFileProvider = new PhysicalFileProvider(Path.Combine(Environment.CurrentDirectory, "wwwroot"));
    app.UseSpaStaticFiles(new StaticFileOptions { FileProvider = spaFileProvider });
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

if (usesLocalStorage)
{
    app.MapGet("/api/files/{**filePath}", async (
        string filePath,
        HttpContext httpContext,
        IConfiguration configuration,
        IStorageProvider storageProvider,
        CancellationToken ct) =>
    {
        var normalized = filePath.TrimStart('/').Replace("\\", "/", StringComparison.Ordinal);
        var signature = httpContext.Request.Query["sig"].ToString();
        var hasValidSignature =
            long.TryParse(httpContext.Request.Query["expires"].ToString(), out var expires) &&
            LocalFileUrlSigner.Validate(
                normalized,
                expires,
                signature,
                configuration["StorageProvider:Local:UrlSigningKey"] ?? configuration["JwtSettings:Secret"],
                DateTimeOffset.UtcNow);
        if (!hasValidSignature)
        {
            return Results.NotFound();
        }

        try
        {
            var stream = await storageProvider.DownloadAsync(normalized, ct);
            return Results.File(
                stream,
                "application/octet-stream",
                Path.GetFileName(normalized),
                enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Results.NotFound();
        }
    }).RequireRateLimiting("public-stream");
}

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
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Swagger:Enabled", false))
    app.UseSwaggerGen();

static async Task<IResult> ReadinessResponse(
    HttpContext context,
    HealthCheckService healthCheckService)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));
    HealthReport report;
    try
    {
        report = await healthCheckService.CheckHealthAsync(timeout.Token);
    }
    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    {
        return Results.Json(
            new { status = "Unhealthy", checks = new { timeout = "Unhealthy" } },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    var result = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Status == HealthStatus.Healthy ? null : "dependency_unavailable"
        })
    };
    var statusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
    return Results.Json(result, statusCode: statusCode);
}

app.MapGet("/api/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapGet("/api/health/ready", ReadinessResponse)
    .AllowAnonymous()
    .RequireRateLimiting("health-read");
app.MapGet("/api/health", ReadinessResponse)
    .AllowAnonymous()
    .RequireRateLimiting("health-read");

// SignalR hub endpoints
app.MapHub<LeaderboardHub>("/hubs/leaderboard").RequireRateLimiting("hub-connect");
app.MapHub<GameHub>("/hubs/game").RequireRateLimiting("hub-connect");
app.MapHub<MonitorHub>("/hubs/monitor").RequireRateLimiting("hub-connect");

app.MapFallback("/api/{**path}", () => Results.NotFound(new
{
    error = "API endpoint not found"
}));

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

static string NormalizeCorsOrigin(string configuredOrigin)
{
    if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var origin) ||
        (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps) ||
        string.IsNullOrWhiteSpace(origin.Host) ||
        !string.IsNullOrEmpty(origin.UserInfo) ||
        origin.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(origin.Query) ||
        !string.IsNullOrEmpty(origin.Fragment))
    {
        throw new InvalidOperationException(
            $"Cors:AllowedOrigins contains an invalid HTTP(S) origin: '{configuredOrigin}'.");
    }

    return origin.GetLeftPart(UriPartial.Authority);
}
