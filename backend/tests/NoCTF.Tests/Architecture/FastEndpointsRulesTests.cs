using System.Reflection;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using NoCTF.API.Endpoints.Submissions;
using NoCTF.Runner.Endpoints;

namespace NoCTF.Tests.Architecture;

public class FastEndpointsRulesTests
{
    [Test]
    public async Task Business_endpoints_use_execute_async_and_typed_result_contracts()
    {
        var endpointTypes = new[]
            {
                typeof(SubmitFlagEndpoint).Assembly,
                typeof(CreateContainerEndpoint).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && InheritsEndpoint(type))
            .ToList();

        var handleOverrides = endpointTypes.Where(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(method => method.Name == nameof(Endpoint<object>.HandleAsync)))
            .Select(type => type.FullName)
            .ToList();

        var missingExecute = endpointTypes.Where(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .All(method => method.Name != nameof(Endpoint<object>.ExecuteAsync)))
            .Select(type => type.FullName)
            .ToList();

        var untypedContracts = endpointTypes
            .Select(type => type.BaseType!.GetGenericArguments()[^1])
            .Where(type => !type.IsGenericType
                || (!type.GetGenericTypeDefinition().Name.StartsWith("Results`", StringComparison.Ordinal)
                    && !type.GetGenericTypeDefinition().Name.StartsWith("Ok`", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToList();

        await Assert.That(handleOverrides).IsEmpty();
        await Assert.That(missingExecute).IsEmpty();
        await Assert.That(untypedContracts).IsEmpty();
    }

    [Test]
    public async Task Fix_archive_upload_uses_strongly_typed_form_file_binding()
    {
        var fileProperty = typeof(UploadFixArchiveRequest).GetProperty(nameof(UploadFixArchiveRequest.File));

        await Assert.That(fileProperty).IsNotNull();
        await Assert.That(fileProperty!.PropertyType).IsEqualTo(typeof(IFormFile));
        await Assert.That(typeof(UploadFixArchiveEndpoint)
            .GetMethod(nameof(UploadFixArchiveEndpoint.ExecuteAsync), BindingFlags.Public | BindingFlags.Instance)
            ?.ToString()).DoesNotContain("HttpContext");
    }

    private static bool InheritsEndpoint(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && (current.GetGenericTypeDefinition() == typeof(Endpoint<>)
                    || current.GetGenericTypeDefinition() == typeof(Endpoint<,>)
                    || current.GetGenericTypeDefinition() == typeof(EndpointWithoutRequest<>)))
                return true;
        }
        return false;
    }
}
