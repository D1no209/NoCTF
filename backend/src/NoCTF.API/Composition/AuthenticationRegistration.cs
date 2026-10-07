using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using NoCTF.API.Security;
using Microsoft.AspNetCore.Authentication;

namespace NoCTF.API.Composition;

public static class AuthenticationRegistration
{
    public const string AccessScheme = JwtBearerDefaults.AuthenticationScheme;
    public const string InternalScheme = "Internal";
    public const string SsoFlowScheme = "SsoFlow";
    public const string MfaFlowScheme = "MfaFlow";

    public static IServiceCollection AddNoCtfAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = ReadKey(configuration["Authentication:SigningKey"], "Authentication:SigningKey");
        var issuer = configuration["Authentication:Issuer"] ?? "NoCTF";
        var audience = configuration["Authentication:Audience"] ?? "NoCTF.Api";
        var runnerKey = ReadKey(configuration["RunnerScoring:SigningKey"], "RunnerScoring:SigningKey");
        var runnerIssuer = configuration["RunnerScoring:Issuer"] ?? "NoCTF.Runner";
        var runnerAudience = configuration["RunnerScoring:Audience"] ?? "NoCTF.ScoringInput";
        services.AddAuthentication(AccessScheme)
            .AddJwtBearer(AccessScheme, options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if ((context.HttpContext.Request.Path.StartsWithSegments("/hubs/v1/competitions")
                                || context.HttpContext.Request.Path.StartsWithSegments(
                                    "/hubs/v1/notifications")
                                || context.HttpContext.Request.Path.StartsWithSegments(
                                    "/hubs/v1/admin/platform-logs"))
                            && context.Request.Query.TryGetValue("access_token", out var token))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<CurrentAccessTokenValidator>();
                        var failure = await validator.ValidateAsync(
                                context.Principal,
                                context.HttpContext.RequestAborted);
                        if (failure is not null) context.Fail(new MfaAuthenticationDeniedException(failure.Value));
                    },
                    OnChallenge = async context =>
                    {
                        if (context.AuthenticateFailure is MfaAuthenticationDeniedException failure)
                        {
                            context.HandleResponse();
                            await NoCTF.API.Endpoints.Authentication.Mfa.MfaEndpointResults.Failure(failure.Failure).ExecuteAsync(context.HttpContext);
                        }
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero
                };
            })
            .AddJwtBearer(InternalScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(runnerKey)),
                    ValidateIssuer = true,
                    ValidIssuer = runnerIssuer,
                    ValidateAudience = true,
                    ValidAudience = runnerAudience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "runner_id"
                };
            })
            .AddScheme<AuthenticationSchemeOptions, SsoFlowAuthenticationHandler>(
                SsoFlowScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, MfaFlowAuthenticationHandler>(MfaFlowScheme, _ => { });
        services.AddScoped<CurrentAccessTokenValidator>();
        services.AddSingleton<SsoBrowserCorrelation>();
        services.AddSingleton<MfaBrowserFlow>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(MfaFlowScheme, policy => policy.AddAuthenticationSchemes(MfaFlowScheme).RequireAuthenticatedUser());
            options.DefaultPolicy = new AuthorizationPolicyBuilder(AccessScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("AwdCheckResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awd:check-result:write")
                .RequireClaim("resource")
                .RequireClaim("runtime_instance_id")
                .RequireClaim("gameplay_fact_id")
                .RequireClaim("deadline"));
            options.AddPolicy("AwdpFixResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awdp:fix-result:write")
                .RequireClaim("resource")
                .RequireClaim("gameplay_fact_id")
                .RequireClaim("runtime_instance_id")
                .RequireClaim("deadline"));
            options.AddPolicy("PatchVerificationResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "patch-verification:result:write")
                .RequireClaim("resource")
                .RequireClaim("gameplay_fact_id")
                .RequireClaim("runtime_instance_id")
                .RequireClaim("deadline"));
            options.AddPolicy("FixArchiveRead", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awdp:fix-archive:read")
                .RequireClaim("gameplay_fact_id"));
        });
        return services;
    }

    private static string ReadKey(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException($"{name} must contain at least 32 UTF-8 bytes.");
        return value;
    }
}
