using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace NoCTF.API.Composition;

public static class EndpointRegistration
{
    public static WebApplication UseNoCtfEndpoints(this WebApplication app)
    {
        app.UseFastEndpoints(options =>
        {
            options.Endpoints.ShortNames = true;
            options.Endpoints.RoutePrefix = "api/v1";
            options.Errors.ContentType = "application/problem+json";
            options.Errors.ProducesMetadataType = typeof(ValidationProblemDetails);
            options.Errors.ResponseBuilder = (failures, context, statusCode) =>
                ApiValidationProblemFactory.Create(
                    failures,
                    statusCode,
                    context.Request.Path);
        });
        app.UseSwaggerGen(settings =>
            settings.PostProcess = (document, _) => document.Servers.Clear());
        return app;
    }
}

public static class ApiValidationProblemFactory
{
    public static ValidationProblemDetails Create(
        IEnumerable<ValidationFailure> failures,
        int statusCode,
        string? instance = null) =>
        new(failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal))
        {
            Status = statusCode,
            Instance = instance
        };
}
