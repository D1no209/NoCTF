using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Runner.Composition;

public static class RunnerSecurity
{
    public static WebApplication UseRunnerSecurity(this WebApplication app, IConfiguration configuration)
    {
        var expected = configuration["Runtime:Runner:ApiKey"];

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next(context);
                return;
            }

            var supplied = context.Request.Headers["X-Runner-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expected))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            if (string.IsNullOrEmpty(supplied)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        return app;
    }
}
