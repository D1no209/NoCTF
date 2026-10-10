using Microsoft.AspNetCore.Diagnostics;
using NoCTF.Application.Admission;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.API.Security;

public sealed class RequestSafetyExceptionHandler(ILogger<RequestSafetyExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is AdmissionRejectedException admission)
        { await RequestAdmissionMiddleware.WriteFailure(context, admission); return true; }
        if (exception is RequestReplayConflictException)
        {
            await ApiProblems.Problem(statusCode: 409, title: ApiMessages.Get(ApiMessageId.RequestSafetyExceptionHandlerTitleRequestSafetyExceptionHandler),
                detail: ApiMessages.Get(ApiMessageId.RequestSafetyExceptionHandlerDetailIdempotencyKey),
                extensions: new Dictionary<string, object?> { ["code"] = "IdempotencyPayloadMismatch" }).ExecuteAsync(context);
            return true;
        }
        if (exception is NoCTF.Application.Challenges.Configuration.ChallengeMaterialMutationException material)
        {
            await ApiProblems.Problem(statusCode: 409, detail: ApiMessages.For(material.Failure),
                extensions: new Dictionary<string, object?> { ["code"] = material.Failure.ToString() }).ExecuteAsync(context);
            return true;
        }
        var cause = exception;
        while (cause.InnerException is not null) cause = cause.InnerException;
        if (!TransactionFailureClassifier.IsRetryable(exception))
            return false;
        logger.LogWarning("Transactional request contention: {ExceptionType}, cause {CauseType}; request {TraceId}.",
            exception.GetType().Name, cause.GetType().Name, context.TraceIdentifier);
        context.Response.Headers.RetryAfter = "1";
        await ApiProblems.Problem(statusCode: 503, title: ApiMessages.Get(ApiMessageId.RequestSafetyExceptionHandlerTitleRequestSafetyExceptionHandler),
            detail: ApiMessages.Get(ApiMessageId.RequestSafetyExceptionHandlerDetailRequestSafetyExceptionHandler),
            extensions: new Dictionary<string, object?> { ["code"] = "TransactionBusy" }).ExecuteAsync(context);
        return true;
    }

}
