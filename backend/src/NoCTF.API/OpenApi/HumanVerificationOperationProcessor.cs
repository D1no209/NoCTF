using NJsonSchema;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;
using NoCTF.API.Security;

namespace NoCTF.API.OpenApi;

internal sealed class HumanVerificationOperationProcessor : IOperationProcessor
{
    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext aspNetCoreContext
            || !aspNetCoreContext.ApiDescription.ActionDescriptor.EndpointMetadata
                .OfType<HumanVerificationMetadata>()
                .Any())
            return true;

        var operation = context.OperationDescription.Operation;
        if (!operation.Parameters.Any(parameter =>
                string.Equals(
                    parameter.Name,
                    HumanVerificationDefaults.HeaderName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = HumanVerificationDefaults.HeaderName,
                Kind = OpenApiParameterKind.Header,
                IsRequired = false,
                Schema = new JsonSchema
                {
                    Type = JsonObjectType.String,
                    MaxLength = HumanVerificationDefaults.MaximumTokenLength
                },
                Description =
                    "One-time token from the provider selected by public platform configuration. Omit only when that provider is None."
            });
        }

        var problemSchema = context.SchemaGenerator.Generate(
            typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
            context.SchemaResolver);
        operation.Responses["403"] = ProblemResponse(
            "Human verification is required or was rejected.",
            problemSchema);
        operation.Responses.TryAdd("503", ProblemResponse(
            "The selected human verification provider is unavailable.",
            problemSchema));
        return true;
    }

    private static OpenApiResponse ProblemResponse(
        string description,
        JsonSchema schema)
    {
        var response = new OpenApiResponse { Description = description };
        response.Content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = schema
        };
        return response;
    }
}
