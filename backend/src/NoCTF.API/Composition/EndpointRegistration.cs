using FastEndpoints;
using FastEndpoints.OpenApi;
using Scalar.AspNetCore;
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
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.AddDocuments("v1"));
        return app;
    }
}

public static class ApiValidationProblemFactory
{
    public static ValidationProblemDetails Create(
        IEnumerable<ValidationFailure> failures,
        int statusCode,
        string? instance = null)
    {
        var messages = failures.Select(failure => (failure.PropertyName, Message: Describe(failure))).ToArray();
        var problem = new ValidationProblemDetails(messages
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.Message.Text)
                    .ToArray(),
                StringComparer.Ordinal))
        {
            Status = statusCode,
            Instance = instance,
            Title = ApiMessages.Text(ApiMessageId.ValidationBinding,
                new Dictionary<string, object?> { ["field"] = "Request" })
        };
        problem.Extensions["errorMessages"] = messages.GroupBy(item => item.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => new
            {
                key = item.Message.Key,
                arguments = item.Message.Arguments ?? ApiMessages.NoArguments
            }).ToArray(), StringComparer.Ordinal);
        return problem;
    }

    public static ApiMessage Describe(ValidationFailure failure)
    {
        var id = ApiMessages.FindKey(failure.ErrorCode) ?? failure.ErrorCode switch
        {
            "NotEmptyValidator" or "NotNullValidator" => ApiMessageId.ValidationRequired,
            "EmailValidator" => ApiMessageId.ValidationEmail,
            "MinimumLengthValidator" => ApiMessageId.ValidationMinimumLength,
            "MaximumLengthValidator" => ApiMessageId.ValidationMaximumLength,
            "LengthValidator" or "ExactLengthValidator" => ApiMessageId.ValidationLength,
            "InclusiveBetweenValidator" or "ExclusiveBetweenValidator" => ApiMessageId.ValidationRange,
            "GreaterThanValidator" => ApiMessageId.ValidationGreaterThan,
            "GreaterThanOrEqualValidator" => ApiMessageId.ValidationGreaterThanOrEqual,
            "LessThanValidator" => ApiMessageId.ValidationLessThan,
            "LessThanOrEqualValidator" => ApiMessageId.ValidationLessThanOrEqual,
            "EqualValidator" => ApiMessageId.ValidationEqual,
            "NotEqualValidator" => ApiMessageId.ValidationNotEqual,
            "EnumValidator" => ApiMessageId.ValidationEnum,
            "RegularExpressionValidator" or "PredicateValidator" or "AsyncPredicateValidator" => ApiMessageId.ValidationFormat,
            _ => ApiMessageId.ValidationBinding
        };
        var placeholders = failure.FormattedMessagePlaceholderValues ?? new Dictionary<string, object>();
        object Value(string name, object fallback) => placeholders.TryGetValue(name, out var value)
            ? value : fallback;
        // Cross-field comparisons use the field identifier, never submitted password/Flag/token text.
        var comparison = Value("ComparisonProperty", string.Empty);
        if (comparison is not string comparisonField || string.IsNullOrWhiteSpace(comparisonField))
        {
            var value = Value("ComparisonValue", string.Empty);
            comparison = value is string ? ApiMessages.Text(ApiMessageId.ValidationComparisonField) : value;
        }
        return ApiMessages.Get(id, ApiMessages.RequiredArguments(id, new Dictionary<string, object?>
        {
            ["field"] = Value("PropertyName", failure.PropertyName),
            ["minimum"] = Value("MinLength", Value("From", 0)),
            ["maximum"] = Value("MaxLength", Value("To", 0)),
            ["comparison"] = comparison
        }));
    }
}
