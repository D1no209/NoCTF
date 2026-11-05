namespace NoCTF.API.Security;

public sealed class PrivateEndpointResponse;

/// <summary>Applied after routing and before endpoint execution, including typed streamed image results.</summary>
public sealed class PrivateEndpointResponseMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<PrivateEndpointResponse>() is not null)
            context.Response.Headers.CacheControl = "private,no-store";
        return next(context);
    }
}
