using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace NoCTF.API.Localization;

public static class LocalizationRegistration
{
    public static IServiceCollection AddNoCtfLocalization(this IServiceCollection services)
    {
        ApiMessages.Initialize();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = [new CultureInfo("en"), new CultureInfo("zh-CN")];
            options.SupportedUICultures = options.SupportedCultures;
            options.ApplyCurrentCultureToResponseHeaders = true;
            options.RequestCultureProviders = [new HeaderCultureProvider()];
        });
        return services;
    }

    private sealed class HeaderCultureProvider : RequestCultureProvider
    {
        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var languages = httpContext.Request.GetTypedHeaders().AcceptLanguage;
            var match = languages?.Where(value => (value.Quality ?? 1) > 0)
                .OrderByDescending(value => value.Quality ?? 1)
                .Select(value => value.Value.Value)
                .Select(value => value is not null && (value.Equals("en", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) ? "en"
                    : value is not null && (value.Equals("zh", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("zh-", StringComparison.OrdinalIgnoreCase)) ? "zh-CN" : null)
                .FirstOrDefault(value => value is not null);
            return Task.FromResult(match is null ? null : new ProviderCultureResult(match));
        }
    }
}
