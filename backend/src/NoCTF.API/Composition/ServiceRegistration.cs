using FastEndpoints;
using FastEndpoints.OpenApi;
using System.IO.Compression;
using System.Security.Claims;
using System.Threading.RateLimiting;
using NoCTF.Application.Observability;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NoCTF.Application.Authentication.Login;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Admission;
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
using NoCTF.Application.Admission;
using NoCTF.API.SignalR.Publishing;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Teams.WriteUps;
using NoCTF.API.Pagination;
using Microsoft.OpenApi;

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
        services.TryAddSingleton(TimeProvider.System);
        services.AddNoCtfLocalization();
        var allowDevelopmentHumanVerification = development
            || configuration.GetValue<bool>("OpenApi:Generating");
        services.AddOptions<HumanVerificationOptions>()
            .Bind(configuration.GetSection(HumanVerificationOptions.SectionName))
            .Validate(options => options.IsValid(allowDevelopmentHumanVerification),
                "HumanVerification configuration is invalid for the selected provider and environment.")
            .ValidateOnStart();
        services.AddOptions<RefreshHttpOptions>()
            .Bind(configuration.GetSection("Authentication"))
            .Validate(options => options.RefreshAllowedOrigins.All(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"),
                "Authentication:RefreshAllowedOrigins must contain absolute HTTP(S) origins.")
            .ValidateOnStart();
        services.AddOptions<PaginationOptions>()
            .Configure(options => options.SigningKey =
                configuration["Pagination:SigningKey"]
                ?? configuration["Authentication:SigningKey"]
                ?? string.Empty)
            .Validate(options => System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Pagination:SigningKey must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        var uploadLimits = new FileUploadLimits(
            configuration.GetValue(
                "Uploads:MaximumAvatarBytes",
                FileUploadLimits.Default.MaximumAvatarBytes),
            configuration.GetValue(
                "Uploads:MaximumWallpaperBytes",
                FileUploadLimits.Default.MaximumWallpaperBytes),
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
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.ProblemDetails.Extensions.ContainsKey("messageKey")
                || context.ProblemDetails.Extensions.ContainsKey("errorMessages")) return;
            context.ProblemDetails.Title = ApiMessages.Text(ApiMessageId.RequestFailed);
            context.ProblemDetails.Extensions["messageKey"] = ApiMessages.Key(ApiMessageId.RequestFailed);
            context.ProblemDetails.Extensions["messageArguments"] = ApiMessages.NoArguments;
        });
        services.AddExceptionHandler<NoCTF.API.Security.RequestSafetyExceptionHandler>();
        services.AddNoCtfStaticAssetDelivery();
        services.AddHttpContextAccessor();
        services.AddScoped<NoCTF.Application.Commands.Idempotency.IRequestCommandKey, NoCTF.API.Security.RequestCommandKey>();
        services.AddScoped<NoCTF.Application.Authentication.Privacy.IRequestSourceAddress, NoCTF.API.Security.RequestSourceAddress>();
        services.AddNoCtfForwardedHeaders(configuration);
        if (endpointAssemblies is null)
            services.AddFastEndpoints();
        else
            services.AddFastEndpoints(options => options.Assemblies = endpointAssemblies);
        services.OpenApiDocument(options =>
        {
            options.DocumentName = "v1";
            options.Title = "NoCTF API";
            options.Version = "v1";
            options.EnableJWTBearerAuth = false;
            options.ConfigureOpenApi = settings =>
            {
                settings.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                settings.AddSchemaTransformer<LocalizedProblemSchemaTransformer>();
                settings.AddDocumentTransformer<OpenApiReferenceDocumentTransformer>();
            };
            options.AddAuth(AuthenticationRegistration.AccessScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Short-lived NoCTF user access token."
            });
            options.AddAuth(AuthenticationRegistration.InternalScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Short-lived internal token restricted to its resource and operation."
            });
        });
        if (includeInfrastructure)
        {
            services.AddNoCtfHumanVerification(configuration, development);
            services.AddNoCtfInfrastructure(configuration, development);
            services.AddScoped<SubmitFlag>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<RegisterUser>();
            services.AddScoped<GetCurrentUser>();
            services.AddScoped<GetPublicUserProfile>();
            services.AddScoped<GetPublicUserProfileCover>();
            services.AddScoped<ReplaceCurrentUserProfileCover>();
            services.AddScoped<ChangePassword>();
            services.AddScoped<LogoutAll>();
            services.AddScoped<RequestEmailVerification>();
            services.AddScoped<ResendEmailVerification>();
            services.AddScoped<VerifyEmail>();
            services.AddScoped<RequestPasswordReset>();
            services.AddScoped<CompletePasswordReset>();
            services.AddScoped<ModerateTeam>();
        }
        else
        {
            services.AddScoped<IGameplayFactIntakeStore, OpenApiGameplayFactStore>();
            services.AddScoped<IPatchUploadStore, OpenApiPatchUploadStore>();
            services.AddScoped<CreatePatchUpload>();
            services.AddScoped<IAwdpDefenseTargetStore, OpenApiAwdpDefenseTargetStore>();
            services.AddScoped<RequestAwdpDefenseTarget>();
            services.AddScoped<IFixArchiveReader, OpenApiFixArchiveReader>();
            services.AddScoped<IGameplayFactStatusReader, OpenApiStatusReader>();
            services.AddScoped<IUserAuthenticationStore, OpenApiAuthenticationStore>();
            services.AddScoped<ICurrentUserProfilePatchStore, OpenApiAuthenticationStore>();
            services.AddScoped<IAccessTokenVersionReader, OpenApiAccessTokenVersionReader>();
            services.AddSingleton<IAccessTokenIssuer, OpenApiTokenIssuer>();
            services.AddScoped<SubmitFlag>();
            services.AddSingleton<IGameplayFactAdmissionModePolicy, OpenApiGameplayFactAdmissionModePolicy>();
            services.AddScoped<LoginUser>();
            services.AddScoped<RefreshAccessToken>();
            services.AddScoped<GetCurrentUser>();
            services.AddScoped<GetPublicUserProfile>();
            services.AddScoped<PatchCurrentUserProfile>();
            services.AddScoped<ReplaceCurrentUserAvatar>();
            services.AddScoped<GetUserAvatar>();
            services.AddScoped<ReplaceCurrentUserWallpaper>();
            services.AddScoped<GetCurrentUserWallpaper>();
            services.AddScoped<UpdateCurrentUserWallpaperPreference>();
            services.AddScoped<ReplaceCurrentUserProfileCover>();
            services.AddScoped<GetPublicUserProfileCover>();
            services.AddScoped<ChangePassword>();
            services.AddScoped<LogoutAll>();
            services.AddScoped<ModerateTeam>();
            services.AddScoped<ITeamModerationStore, OpenApiModerationStore>();
            services.AddScoped<ICompetitionModerationAuthorizer, OpenApiModerationAuthorizer>();
            services.AddSingleton<IBackendMessagePublisher, OpenApiBackendMessagePublisher>();
            services.AddScoped<ILeaderboardCache, OpenApiLeaderboardCache>();
            services.AddSingleton<FluentStorage.Storage.IStore>(
                _ => FluentStorage.StorageFactory.InMemory());
            services.AddScoped<IManagedFileUploadRegistry, OpenApiManagedFileUploadRegistry>();
            services.AddScoped<ManagedFileUploads>();
            services.AddSingleton<IAvatarImageProcessor, OpenApiAvatarImageProcessor>();
            services.AddSingleton<IWallpaperImageProcessor, OpenApiWallpaperImageProcessor>();
            services.AddScoped<IPasswordResetStore, OpenApiPasswordResetStore>();
            services.AddScoped<RequestPasswordReset>();
            services.AddScoped<CompletePasswordReset>();
            services.AddScoped<IEmailVerificationStore, OpenApiEmailVerificationStore>();
            services.AddScoped<RequestEmailVerification>();
            services.AddScoped<ResendEmailVerification>();
            services.AddScoped<VerifyEmail>();
        }
        var requestAdmissionLimits = configuration
            .GetSection("RequestAdmission")
            .Get<RequestAdmissionOptions>() ?? new RequestAdmissionOptions();
        services.AddSignalR();
        services.AddSingleton<CompetitionHubSubscriptionRegistry>();
        services.AddSingleton<ICompetitionHubAudienceAccess, CompetitionHubAudienceAccess>();
        services.AddSingleton<ICompetitionHubAudienceRouter, CompetitionHubAudienceRouter>();
        services.AddSingleton<ICompetitionHubAudienceCoordinator, CompetitionHubAudienceCoordinator>();
        services.AddScoped<IGameplayFactStatePublisher, SignalRGameplayFactStatePublisher>();
        if (includeInfrastructure && development)
        {
            services.AddSingleton<ILeaderboardRefreshPublisher,
                LocalLeaderboardRefreshPublisher>();
            services.AddSingleton<IGameplayFactStateChangedNotification,
                LocalGameplayFactStatePublisher>();
            services.Replace(ServiceDescriptor.Singleton<INotificationChangePublisher,
                LocalNotificationChangePublisher>());
        }
        services.AddSingleton<NoCTF.API.Pagination.SignedKeysetCursor>();
        services.AddSingleton<TeamWriteUpPreviewTicketCodec>();
        services.AddScoped<ICompetitionLifecycleNotificationPublisher, SignalRCompetitionLifecyclePublisher>();
        if (includeInfrastructure
            && !development
            && !configuration.GetValue<bool>("OpenApi:Generating"))
        {
            services.AddHostedService<NoCTF.API.SignalR.Publishing.NatsGameplayFactStateRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.NatsLeaderboardRefreshRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.NatsPlatformLogRelay>();
            services.AddHostedService<NoCTF.API.SignalR.Publishing.NatsCompetitionEventRefreshRelay>();
            services.AddHostedService<NatsNotificationChangeRelay>();
        }
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                var route = (context.HttpContext.GetEndpoint() as RouteEndpoint)?
                    .RoutePattern.RawText ?? "unmatched";
                NoCtfTelemetry.RecordRateLimitRejection(route);
                context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "60";
                await ApiProblems.Problem(statusCode: 429, title: ApiMessages.Get(ApiMessageId.ServiceRegistrationTitleServiceRegistration), detail: ApiMessages.Get(ApiMessageId.ServiceRegistrationDetailRetry),
                    extensions: new Dictionary<string, object?> { ["code"] = "RateLimited" }).ExecuteAsync(context.HttpContext);
            };
            options.AddPolicy("submission", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub")
                        ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = requestAdmissionLimits.SubmissionPerUserPerMinute,
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
            options.AddPolicy("email-verification-request", context =>
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

    public static IServiceCollection AddNoCtfStaticAssetDelivery(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes
                .Concat(["image/svg+xml"])
                .Distinct(StringComparer.OrdinalIgnoreCase);
        });
        services.Configure<BrotliCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);
        return services;
    }
}
