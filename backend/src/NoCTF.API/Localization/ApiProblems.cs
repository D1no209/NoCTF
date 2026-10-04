using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using FluentValidation.Results;
using NoCTF.API.Composition;

namespace NoCTF.API.Localization;

public static class ApiProblems
{
    public static ValidationProblem ValidationProblem(IDictionary<string, Enum?> failures)
    {
        var problem = ApiValidationProblemFactory.Create(failures.Select(item => new ValidationFailure(item.Key, string.Empty)
        {
            ErrorCode = ApiMessages.For(item.Value).Key
        }), StatusCodes.Status400BadRequest);
        return TypedResults.ValidationProblem(problem.Errors, title: problem.Title, extensions: problem.Extensions);
    }
    public static ProblemHttpResult Problem(ProblemDetails problem) => TypedResults.Problem(problem);
    public static ProblemHttpResult Problem(
        int? statusCode = null,
        ApiMessage? title = null,
        ApiMessage? detail = null,
        string? type = null,
        string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = Create(statusCode ?? 500, title, detail, instance, extensions);
        problem.Type = type;
        return TypedResults.Problem(problem);
    }

    public static ProblemDetails Create(
        int statusCode,
        ApiMessage? title = null,
        ApiMessage? detail = null,
        string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        var message = detail ?? title ?? ApiMessages.Get(ApiMessageId.RequestFailed);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title?.Text ?? ApiMessages.Text(ApiMessageId.RequestFailed),
            Detail = detail?.Text,
            Instance = instance
        };
        if (extensions is not null)
            foreach (var (key, value) in extensions) problem.Extensions[key] = value;
        problem.Extensions["messageKey"] = message.Key;
        problem.Extensions["messageArguments"] = message.Arguments ?? ApiMessages.NoArguments;
        return problem;
    }
}
