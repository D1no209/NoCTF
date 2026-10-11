namespace NoCTF.API.Endpoints.Theory;

public sealed class TheoryPrivateResponse;

/// <summary>Applied after routing and before endpoint execution, including typed streamed image results.</summary>
public sealed class TheoryResponsePrivacy(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<TheoryPrivateResponse>() is not null)
            context.Response.Headers.CacheControl = "private,no-store";
        return next(context);
    }
}
