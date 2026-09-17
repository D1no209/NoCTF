namespace NoCTF.API.Composition;

public static class PipelineConfiguration
{
    public static WebApplication UseNoCtfPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseNoCtfStaticAssetDelivery();
        app.UseExceptionHandler();
        app.UseAuthentication();
        if (app.Services.GetService<IServiceProviderIsService>() is { } serviceCatalog
            && serviceCatalog.IsService(typeof(NoCTF.Application.Competitions.Access.ICompetitionAudienceReader))
            && serviceCatalog.IsService(typeof(NoCTF.Application.Teams.Moderation.ICompetitionModerationAuthorizer)))
        {
            app.UseMiddleware<Security.CompetitionAudienceGateMiddleware>();
        }
        app.UseRateLimiter();
        app.UseAuthorization();
        // Isolated transport tests intentionally omit Infrastructure; production/combined hosts register admission.
        if (app.Services.GetService<IServiceProviderIsService>()?.IsService(typeof(NoCTF.Application.Admission.IRequestAdmission)) == true)
            app.UseMiddleware<Security.RequestAdmissionMiddleware>();
        app.UseMiddleware<Security.EmailVerificationGateMiddleware>();
        if (app.Services.GetService<IServiceProviderIsService>()?.IsService(typeof(NoCTF.Application.Admission.IHumanVerificationVerifier)) == true)
            app.UseMiddleware<Security.HumanVerificationMiddleware>();
        app.UseMiddleware<SpaDocumentMetadataMiddleware>();
        return app;
    }

    public static WebApplication UseNoCtfStaticAssetDelivery(this WebApplication app)
    {
        app.UseResponseCompression();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                if (path.StartsWithSegments("/_nuxt"))
                {
                    context.Context.Response.Headers.CacheControl =
                        "public,max-age=31536000,immutable";
                }
                else if (string.Equals(
                    context.File.Name,
                    "index.html",
                    StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                }
            }
        });
        return app;
    }
}
