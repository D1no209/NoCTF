using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Composition;

public sealed class SpaDocumentMetadataMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IWebHostEnvironment environment,
        LinkGenerator links)
    {
        await next(context);
        if (!ShouldServeSpaDocument(context)
            || string.IsNullOrWhiteSpace(environment.WebRootPath))
            return;

        var indexPath = Path.Combine(environment.WebRootPath, "index.html");
        if (!File.Exists(indexPath))
            return;

        var document = await File.ReadAllTextAsync(indexPath, context.RequestAborted);
        var headEnd = document.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0)
            return;

        var platformConfiguration = context.RequestServices
            .GetService<ManagePlatformConfiguration>();
        var metadata = string.Empty;
        if (platformConfiguration is not null)
        {
            var configuration = await platformConfiguration.GetAsync(context.RequestAborted);
            var pageUrl = context.Request.GetDisplayUrl();
            var siteUrl = UriHelper.BuildAbsolute(
                context.Request.Scheme,
                context.Request.Host,
                context.Request.PathBase,
                "/");
            var logoPath = PublicPlatformConfigurationMapping.LogoUrl(
                configuration,
                links,
                context);
            var logoUrl = logoPath is null
                ? null
                : new Uri(new Uri(siteUrl, UriKind.Absolute), logoPath.TrimStart('/')).AbsoluteUri;
            metadata = RenderMetadata(
                configuration.Name,
                configuration.Description,
                pageUrl,
                siteUrl,
                logoUrl);
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Vary = "Accept";
        await context.Response.WriteAsync(
            document.Insert(headEnd, metadata),
            Encoding.UTF8,
            context.RequestAborted);
    }

    private static bool ShouldServeSpaDocument(HttpContext context)
    {
        var path = context.Request.Path;
        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || context.Response.HasStarted
            || !HttpMethods.IsGet(context.Request.Method)
            || path.HasValue && Path.HasExtension(path.Value)
            || path.StartsWithSegments("/api")
            || path.StartsWithSegments("/hubs")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/swagger"))
            return false;

        var accept = context.Request.GetTypedHeaders().Accept;
        return accept is null
            || accept.Count == 0
            || accept.Any(value =>
                string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.MediaType.Value, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.MediaType.Value, "*/*", StringComparison.Ordinal));
    }

    private static string RenderMetadata(
        string name,
        string? description,
        string pageUrl,
        string siteUrl,
        string? logoUrl)
    {
        var encodedName = WebUtility.HtmlEncode(name);
        var encodedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : WebUtility.HtmlEncode(description);
        var encodedPageUrl = WebUtility.HtmlEncode(pageUrl);
        var builder = new StringBuilder()
            .Append("<!-- noctf-platform-metadata -->")
            .Append("<title>").Append(encodedName).Append("</title>")
            .Append("<meta property=\"og:title\" content=\"").Append(encodedName).Append("\">")
            .Append("<meta property=\"og:type\" content=\"website\">")
            .Append("<meta property=\"og:site_name\" content=\"").Append(encodedName).Append("\">")
            .Append("<meta property=\"og:url\" content=\"").Append(encodedPageUrl).Append("\">")
            .Append("<meta name=\"twitter:card\" content=\"summary\">")
            .Append("<meta name=\"twitter:title\" content=\"").Append(encodedName).Append("\">");
        if (encodedDescription is not null)
        {
            builder
                .Append("<meta name=\"description\" content=\"").Append(encodedDescription).Append("\">")
                .Append("<meta property=\"og:description\" content=\"").Append(encodedDescription).Append("\">")
                .Append("<meta name=\"twitter:description\" content=\"").Append(encodedDescription).Append("\">");
        }
        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            var encodedLogoUrl = WebUtility.HtmlEncode(logoUrl);
            builder
                .Append("<meta property=\"og:image\" content=\"").Append(encodedLogoUrl).Append("\">")
                .Append("<meta name=\"twitter:image\" content=\"").Append(encodedLogoUrl).Append("\">");
        }

        var structuredData = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = name,
            ["description"] = string.IsNullOrWhiteSpace(description) ? null : description,
            ["url"] = siteUrl
        });
        return builder
            .Append("<script type=\"application/ld+json\">")
            .Append(structuredData)
            .Append("</script>")
            .ToString();
    }
}
