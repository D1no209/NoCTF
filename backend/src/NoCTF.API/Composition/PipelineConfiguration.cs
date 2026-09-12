namespace NoCTF.API.Composition;

public static class PipelineConfiguration
{
    public static WebApplication UseNoCtfPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseStaticFiles();
        app.UseExceptionHandler();
        app.UseAuthentication();
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
}
