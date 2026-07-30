using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Mvc;

namespace NoCTF.API.Composition;

public static class EndpointRegistration
{
    public static WebApplication UseNoCtfEndpoints(this WebApplication app)
    {
        app.UseFastEndpoints(options =>
        {
            options.Endpoints.RoutePrefix = "api/v1";
            options.Errors.ResponseBuilder = (failures, context, statusCode) =>
            {
                var problem = new ValidationProblemDetails { Status = statusCode, Instance = context.Request.Path };
                foreach (var failure in failures)
                    problem.Errors.Add(failure.PropertyName, [failure.ErrorMessage]);
                return problem;
            };
        });
        app.UseSwaggerGen(settings =>
            settings.PostProcess = (document, _) => document.Servers.Clear());
        return app;
    }
}
