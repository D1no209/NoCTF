using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using NoCTF.API.Security;

namespace NoCTF.API.Composition;

public static class AuthenticationRegistration
{
    public const string RunnerScoringScheme = "RunnerScoringBearer";

    public static IServiceCollection AddNoCtfAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = ReadKey(configuration["Authentication:SigningKey"], "Authentication:SigningKey");
        var issuer = configuration["Authentication:Issuer"] ?? "NoCTF";
        var audience = configuration["Authentication:Audience"] ?? "NoCTF.Api";
        var runnerKey = ReadKey(configuration["RunnerScoring:SigningKey"], "RunnerScoring:SigningKey");
        var runnerIssuer = configuration["RunnerScoring:Issuer"] ?? "NoCTF.Runner";
        var runnerAudience = configuration["RunnerScoring:Audience"] ?? "NoCTF.ScoringInput";
        var clockSkew = TimeSpan.FromSeconds(configuration.GetValue("Authentication:ClockSkewSeconds", 30));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/competition")
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
                    ClockSkew = clockSkew
                };
            })
            .AddJwtBearer(RunnerScoringScheme, options =>
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
                    ClockSkew = clockSkew,
                    NameClaimType = "runner_id"
                };
            });
        services.AddScoped<IAuthorizationHandler, CurrentTokenVersionHandler>();
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new CurrentTokenVersionRequirement())
                .Build();
            options.AddPolicy("ScoringInput", policy => policy
                .AddAuthenticationSchemes(RunnerScoringScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "scoring.write"));
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
