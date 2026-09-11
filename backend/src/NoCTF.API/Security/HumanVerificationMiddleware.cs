using Microsoft.Extensions.Options;
using NoCTF.API.Serialization;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Observability;
using System.Text.Json.Serialization;

namespace NoCTF.API.Security;

public static class HumanVerificationDefaults
{
    public const string HeaderName = "X-NoCTF-Human-Verification";
    public const int MaximumTokenLength = 4_096;
}

public sealed record HumanVerificationMetadata(HumanVerificationAction Action);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<HumanVerificationProblemCode>))]
internal enum HumanVerificationProblemCode
{
    HumanVerificationRequired,
    HumanVerificationFailed,
    HumanVerificationUnavailable
}

public sealed class HumanVerificationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IHumanVerificationVerifier verifier,
        IRequestSourceAddress sourceAddress,
        IOptions<HumanVerificationOptions> configuredOptions)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<HumanVerificationMetadata>();
        var provider = configuredOptions.Value.Provider;
        if (metadata is null || provider == HumanVerificationProvider.None)
        {
            await next(context);
            return;
        }

        var tokens = context.Request.Headers[HumanVerificationDefaults.HeaderName];
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
        {
            NoCtfTelemetry.RecordHumanVerification(
                provider.ToString(),
                metadata.Action.ToString(),
                HumanVerificationResult.Rejected.ToString());
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                HumanVerificationProblemCode.HumanVerificationRequired,
                "Human verification is required.",
                "Complete human verification before retrying this operation.");
            return;
        }
        if (tokens.Count != 1 || tokens[0]!.Length > HumanVerificationDefaults.MaximumTokenLength)
        {
            NoCtfTelemetry.RecordHumanVerification(
                provider.ToString(),
                metadata.Action.ToString(),
                HumanVerificationResult.Rejected.ToString());
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                HumanVerificationProblemCode.HumanVerificationFailed,
                "Human verification failed.",
                "The verification token is invalid, expired, or does not match this operation.");
            return;
        }

        var result = await verifier.VerifyAsync(
            new HumanVerificationAttempt(tokens[0]!, metadata.Action, sourceAddress.Address),
            context.RequestAborted);
        NoCtfTelemetry.RecordHumanVerification(
            provider.ToString(),
            metadata.Action.ToString(),
            result.ToString());

        if (result == HumanVerificationResult.Verified)
        {
            await next(context);
            return;
        }

        if (result == HumanVerificationResult.Rejected)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                HumanVerificationProblemCode.HumanVerificationFailed,
                "Human verification failed.",
                "The verification token is invalid, expired, or does not match this operation.");
            return;
        }

        context.Response.Headers.RetryAfter = "5";
        await WriteProblemAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            HumanVerificationProblemCode.HumanVerificationUnavailable,
            "Human verification is temporarily unavailable.",
            "The selected verification provider could not validate this request. Try again with a new verification token.");
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        int status,
        HumanVerificationProblemCode code,
        string title,
        string detail) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            }).ExecuteAsync(context);
}
