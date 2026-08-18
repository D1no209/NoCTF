using FastEndpoints;
using FastEndpoints.Swagger;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.Application.Authentication.Login;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Infrastructure;
using NoCTF.API.OpenApi;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Storage;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.API.SignalR.Publishing;
using NSwag;

namespace NoCTF.API.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddNoCtfApi(this IServiceCollection services, IConfiguration configuration)
        => AddNoCtfApi(services, configuration, true, development: false);

    public static IServiceCollection AddNoCtfApi(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeInfrastructure,
        bool development = false,
        IReadOnlyCollection<System.Reflection.Assembly>? endpointAssemblies = null)
    {
        var uploadLimits = new FileUploadLimits(
            configuration.GetValue(
                "Uploads:MaximumAvatarBytes",
                FileUploadLimits.Default.MaximumAvatarBytes),
            configuration.GetValue(
                "Uploads:MaximumLogoBytes",
                FileUploadLimits.Default.MaximumLogoBytes),
            configuration.GetValue(
                "Uploads:MaximumPosterBytes",
                FileUploadLimits.Default.MaximumPosterBytes),
            configuration.GetValue(
                "Uploads:MaximumAttachmentBytes",
                FileUploadLimits.Default.MaximumAttachmentBytes));
        var uploadLimitErrors = uploadLimits.Validate();
        if (uploadLimitErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", uploadLimitErrors));
        services.AddSingleton(uploadLimits);
        services.AddProblemDetails();
        services.AddNoCtfForwardedHeaders(configuration);
        if (endpointAssemblies is null)
            services.AddFastEndpoints();
        else
            services.AddFastEndpoints(options => options.Assemblies = endpointAssemblies);
        services.SwaggerDocument(options =>
        {
            options.EnableJWTBearerAuth = false;
            options.DocumentSettings = settings =>
            {
                settings.SchemaSettings.ResolveExternalXmlDocumentation = false;
                settings.DocumentProcessors.Add(
                    new AwdpFixResultOutcomeDocumentProcessor());
                settings.OperationProcessors.Add(
                    new CompetitionChallengeRevisionOperationProcessor());
                settings.AddAuth("Bearer", new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Short-lived NoCTF user access token."
                }, []);
                settings.AddAuth("RunnerScoringBearer", new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Short-lived Runner token restricted to scoring.write."
                }, []);
            };
        });
        if (includeInfrastructure)
        {
            services.AddNoCtfInfrastructure(configuration, development);
            services.AddScoped<SubmitFlag>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<RegisterUser>();
            services.AddScoped<GetCurrentUser>();
            services.AddScoped<GetPublicUserProfile>();
            services.AddScoped<ChangePassword>();
            services.AddScoped<LogoutAll>();
            services.AddScoped<ResendEmailVerification>();
            services.AddScoped<VerifyEmail>();
            services.AddScoped<RequestPasswordReset>();
            services.AddScoped<CompletePasswordReset>();
            services.AddScoped<ModerateTeam>();
        }
        else
        {
            services.AddScoped<IGameplayFactIntakeStore, SwaggerGameplayFactStore>();
            services.AddScoped<IPatchUploadStore, SwaggerPatchUploadStore>();
            services.AddScoped<CreatePatchUpload>();
            services.AddScoped<IAwdpDefenseTargetStore, SwaggerAwdpDefenseTargetStore>();
            services.AddScoped<RequestAwdpDefenseTarget>();
            services.AddScoped<IFixArchiveReader, SwaggerFixArchiveReader>();
            services.AddScoped<IGameplayFactStatusReader, SwaggerStatusReader>();
            services.AddScoped<IUserAuthenticationStore, SwaggerAuthenticationStore>();
            services.AddScoped<IAccessTokenVersionReader, SwaggerAccessTokenVersionReader>();
            services.AddSingleton<IAccessTokenIssuer, SwaggerTokenIssuer>();
            services.AddScoped<SubmitFlag>();
            services.AddSingleton<IGameplayFactAdmissionModePolicy, SwaggerGameplayFactAdmissionModePolicy>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<GetCurrentUser>();
            services.AddScoped<GetPublicUserProfile>();
            services.AddScoped<UpdateCurrentUserProfile>();
            services.AddScoped<ReplaceCurrentUserAvatar>();
            services.AddScoped<GetUserAvatar>();
            services.AddScoped<ChangePassword>();
            services.AddScoped<LogoutAll>();
            services.AddScoped<ModerateTeam>();
            services.AddScoped<ITeamModerationStore, SwaggerModerationStore>();
            services.AddScoped<ICompetitionModerationAuthorizer, SwaggerModerationAuthorizer>();
            services.AddSingleton<IBackendMessagePublisher, SwaggerBackendMessagePublisher>();
            services.AddScoped<ILeaderboardCache, SwaggerLeaderboardCache>();
            services.AddSingleton<IObjectStorage, SwaggerObjectStorage>();
            services.AddScoped<IManagedFileUploadRegistry, SwaggerManagedFileUploadRegistry>();
            services.AddScoped<ManagedFileUploads>();
            services.AddSingleton<IAvatarImageProcessor, SwaggerAvatarImageProcessor>();
            services.AddScoped<IPasswordResetStore, SwaggerPasswordResetStore>();
            services.AddScoped<RequestPasswordReset>();
            services.AddScoped<CompletePasswordReset>();
        }
        var redis = configuration.GetConnectionString("Redis");
        var signalR = services.AddSignalR();
        services.AddScoped<IGameplayFactStatePublisher, SignalRGameplayFactStatePublisher>();
        if (includeInfrastructure && development)
        {
            services.AddSingleton<ILeaderboardRefreshPublisher,
                LocalLeaderboardRefreshPublisher>();
            services.AddSingleton<IGameplayFactStateChangedNotification,
                LocalGameplayFactStatePublisher>();
        }
        services.AddSingleton<NoCTF.API.Pagination.SignedKeysetCursor>();
        services.AddScoped<ICompetitionLifecycleNotificationPublisher, SignalRCompetitionLifecyclePublisher>();
        if (includeInfrastructure
            && !development
            && !configuration.GetValue<bool>("OpenApi:Exporting")
            && !string.IsNullOrWhiteSpace(redis))
        {
            signalR.AddStackExchangeRedis(redis);
            services.AddHostedService<NoCTF.API.SignalR.Publishing.RedisGameplayFactStateRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.RedisLeaderboardRefreshRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.RedisPlatformLogRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.RedisCompetitionEventRefreshRelay>();
        }
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("submission", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub")
                        ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddPolicy("avatar", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub")
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddPolicy("question", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub")
                        ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddPolicy("password-reset-request", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));
        });
        return services;
    }
}
