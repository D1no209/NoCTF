using FastEndpoints;
using FastEndpoints.Swagger;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.Application.Authentication.Login;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Infrastructure;
using NoCTF.API.OpenApi;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Application.Storage;
using NoCTF.Application.Notifications;
using NoCTF.API.SignalR.Publishing;

namespace NoCTF.API.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfApi(this IServiceCollection services, IConfiguration configuration)
        => AddNoCtfApi(services, configuration, true);

    public static IServiceCollection AddNoCtfApi(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeInfrastructure)
    {
        services.AddProblemDetails();
        services.AddFastEndpoints();
        services.SwaggerDocument();
        if (includeInfrastructure)
        {
            services.AddNoCtfInfrastructure(configuration);
            services.AddScoped<SubmitFlag>();
            services.AddScoped<SubmitFix>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<ModerateTeam>();
            services.AddScoped<CreateFixUpload>();
        }
        else
        {
            services.AddScoped<ISubmissionIntakeStore, SwaggerSubmissionStore>();
            services.AddScoped<ISubmissionQueue, SwaggerSubmissionQueue>();
            services.AddScoped<ISubmissionStatusReader, SwaggerStatusReader>();
            services.AddScoped<IUserAuthenticationStore, SwaggerAuthenticationStore>();
            services.AddSingleton<IAccessTokenIssuer, SwaggerTokenIssuer>();
            services.AddScoped<SubmitFlag>();
            services.AddScoped<SubmitFix>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<ModerateTeam>();
            services.AddScoped<CreateFixUpload>();
            services.AddScoped<ITeamModerationStore, SwaggerModerationStore>();
            services.AddScoped<ICompetitionModerationAuthorizer, SwaggerModerationAuthorizer>();
            services.AddScoped<IScoringRebuildQueue, SwaggerRebuildQueue>();
            services.AddScoped<ILeaderboardStore, SwaggerLeaderboardStore>();
            services.AddScoped<IFixUploadSessionStore, SwaggerFixUploadStore>();
            services.AddSingleton<IObjectStorage, SwaggerObjectStorage>();
        }
        var redis = configuration.GetConnectionString("Redis");
        var signalR = services.AddSignalR();
        services.AddScoped<ISubmissionResultPublisher, SignalRSubmissionResultPublisher>();
        if (includeInfrastructure && !string.IsNullOrWhiteSpace(redis))
        {
            signalR.AddStackExchangeRedis(redis);
            services.AddHostedService<NoCTF.API.SignalR.Publishing.RedisSubmissionResultRelay>();
        }
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("submission", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.User.FindFirst("sub")?.Value ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        return services;
    }
}
