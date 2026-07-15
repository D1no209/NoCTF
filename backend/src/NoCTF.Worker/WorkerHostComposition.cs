using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Notifications;
using NoCTF.Application.Plugins;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Runner.Client;
using StackExchange.Redis;

namespace NoCTF.Worker;

public static class WorkerHostComposition
{
    public static IHostApplicationBuilder AddNoCtfWorkerServices(this IHostApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
            if (SecretValueValidator.IsConnectionStringUnsafe(defaultConnection))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection contains a missing, weak, or placeholder value.");

            StorageProviderFactory.ValidateLocalUrlSigningKey(builder.Configuration);
        }

        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
        });

        builder.Services.AddSingleton<IStorageProvider>(_ => StorageProviderFactory.Create(builder.Configuration));
        var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"];
        if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(runnerBaseUrl))
            throw new InvalidOperationException("Runner:BaseUrl must be configured outside Development.");
        Uri? runnerBaseUri = null;
        if (!string.IsNullOrWhiteSpace(runnerBaseUrl) &&
            (!Uri.TryCreate(runnerBaseUrl, UriKind.Absolute, out runnerBaseUri) ||
             (runnerBaseUri.Scheme != Uri.UriSchemeHttp && runnerBaseUri.Scheme != Uri.UriSchemeHttps) ||
             !string.IsNullOrEmpty(runnerBaseUri.UserInfo)))
        {
            throw new InvalidOperationException(
                "Runner:BaseUrl must be an absolute HTTP(S) URL without embedded credentials.");
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

        builder.Services.AddNoCtfApplicationCore();
        var redisConnection = builder.Configuration.GetConnectionString("Redis");
        if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(redisConnection))
            throw new InvalidOperationException("ConnectionStrings:Redis must be configured outside Development.");
        redisConnection ??= "localhost:6379";
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = builder.Environment.IsProduction();
            return ConnectionMultiplexer.Connect(options);
        });
        builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
        builder.Services.AddScoped<ILeaderboardProjectionBuilder>(sp =>
            (LeaderboardService)sp.GetRequiredService<ILeaderboardService>());
        builder.Services.AddSingleton<IRedisLeaderboardCache, RedisLeaderboardCache>();
        builder.Services.AddSingleton<IHubNotifierService, RedisStreamHubNotifier>();
        builder.Services.AddScoped<IBackgroundTaskQueue, BackgroundTaskQueue>();
        builder.Services.AddHostedService<CompetitionJobRegistryStartupValidator>();
        PluginLoader.LoadAndRegisterAll(
            builder.Services,
            builder.Configuration,
            PluginHostRole.Worker);
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddHostedService<ExpiredInstanceCleanupService>();
        builder.Services.AddHostedService<StorageCleanupService>();

        return builder;
    }
}
