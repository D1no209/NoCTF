namespace NoCTF.API.Composition;

public static class PipelineConfiguration
{
    public static WebApplication UseNoCtfPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
