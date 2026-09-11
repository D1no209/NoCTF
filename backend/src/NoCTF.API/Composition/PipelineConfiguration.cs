namespace NoCTF.API.Composition;

public static class PipelineConfiguration
{
    public static WebApplication UseNoCtfPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseDefaultFiles();
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
        app.Use(async (context, next) =>
        {
            await next();
            var path = context.Request.Path;
            var acceptsHtml = context.Request.GetTypedHeaders().Accept?
                .Any(value => string.Equals(
                    value.MediaType.Value,
                    "text/html",
                    StringComparison.OrdinalIgnoreCase)) == true;
            var webRoot = app.Environment.WebRootPath;
            if (context.Response.StatusCode != StatusCodes.Status404NotFound
                || context.Response.HasStarted
                || !HttpMethods.IsGet(context.Request.Method)
                || !acceptsHtml
                || path.HasValue && Path.HasExtension(path.Value)
                || path.StartsWithSegments("/api")
                || path.StartsWithSegments("/hubs")
                || path.StartsWithSegments("/health")
                || path.StartsWithSegments("/openapi")
                || path.StartsWithSegments("/swagger")
                || string.IsNullOrWhiteSpace(webRoot))
                return;

            var indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
                return;

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexPath, context.RequestAborted);
        });
        return app;
    }
}
