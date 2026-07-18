using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace NoCTF.API.Composition;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddNoCtfAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["Authentication:SigningKey"]
            ?? throw new InvalidOperationException("Authentication:SigningKey is required.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();
        return services;
    }
}
