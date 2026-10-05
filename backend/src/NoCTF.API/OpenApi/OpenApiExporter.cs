using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;

namespace NoCTF.API.OpenApi;

public static class OpenApiExporter
{
    public static async Task ExportAsync(
        WebApplication app,
        OpenApiDocumentRegistration registration,
        IApiDescriptionGroupCollectionProvider descriptions)
    {
        await app.StartAsync();
        var generator = new AspNetCoreOpenApiDocumentGenerator(registration.Settings);
        var document = await generator.GenerateAsync(descriptions.ApiDescriptionGroups);
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            app.Environment.ContentRootPath,
            "..",
            ".."));
        var artifactDirectory = Path.Combine(repositoryRoot, "artifacts", "openapi");
        var staticDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "NoCTF.API",
            "wwwroot",
            "openapi");
        Directory.CreateDirectory(artifactDirectory);
        Directory.CreateDirectory(staticDirectory);
        var json = document.ToJson().ReplaceLineEndings("\n").TrimEnd() + "\n";
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "swagger.json"), json);
        await File.WriteAllTextAsync(Path.Combine(staticDirectory, "v1.json"), json);
        await app.StopAsync();
    }
}
