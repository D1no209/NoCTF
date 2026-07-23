using System.Reflection;
using FastEndpoints;
using NoCTF.API.Endpoints.Submissions;

namespace NoCTF.Tests.Architecture;

public class FastEndpointsRulesTests
{
    [Test]
    public async Task Business_endpoints_use_execute_async_and_typed_result_contracts()
    {
        var endpointTypes = new[] { typeof(SubmitFlagEndpoint).Assembly }
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
