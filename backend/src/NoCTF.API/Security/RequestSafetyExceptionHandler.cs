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
            await TypedResults.Problem(statusCode: 409, title: "请求标识冲突",
                detail: "该 Idempotency-Key 已用于不同的请求内容，请为新的操作生成新的标识。",
                extensions: new Dictionary<string, object?> { ["code"] = "IdempotencyPayloadMismatch" }).ExecuteAsync(context);
            return true;
        }
        var cause = exception;
        while (cause.InnerException is not null) cause = cause.InnerException;
        if (exception is not FeatureCriticalSectionTimeoutException
            && cause is not System.Data.Common.DbException { SqlState: "40P01" or "55P03" or "40001" })
            return false;
        logger.LogWarning("Transactional request contention: {ExceptionType}, SQLSTATE {SqlState}; request {TraceId}.",
            exception.GetType().Name, (cause as System.Data.Common.DbException)?.SqlState, context.TraceIdentifier);
        context.Response.Headers.RetryAfter = "1";
        await TypedResults.Problem(statusCode: 503, title: "操作正在竞争资源",
            detail: "本次事务未完成，请稍后使用相同的请求标识重试。",
            extensions: new Dictionary<string, object?> { ["code"] = "TransactionBusy" }).ExecuteAsync(context);
        return true;
    }
}
