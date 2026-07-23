using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using NoCTF.API.Security;

namespace NoCTF.API.Composition;

public static class AuthenticationRegistration
{
    public const string AccessScheme = JwtBearerDefaults.AuthenticationScheme;
    public const string InternalScheme = "Internal";

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
                        if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/v1/competitions")
                            && context.Request.Query.TryGetValue("access_token", out var token))
                            context.Token = token;
                        return Task.CompletedTask;
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
            });
        services.AddScoped<IAuthorizationHandler, CurrentTokenVersionHandler>();
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(AccessScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new CurrentTokenVersionRequirement())
                .Build();
            options.AddPolicy("AwdCheckResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awd:check-result:write")
                .RequireClaim("resource")
                .RequireClaim("runtime_instance_id")
                .RequireClaim("generation")
                .RequireClaim("checker_sequence")
                .RequireClaim("processing_version")
                .RequireClaim("deadline"));
            options.AddPolicy("AwdpFixResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awdp:fix-result:write")
                .RequireClaim("submission_id")
                .RequireClaim("processing_version")
                .RequireClaim("deadline"));
            options.AddPolicy("FixArchiveRead", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awdp:fix-archive:read")
                .RequireClaim("submission_id"));
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
