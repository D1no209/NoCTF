using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NoCTF.API.OpenApi;

/// <summary>Describes the localization fields emitted through ProblemDetails.Extensions.</summary>
public sealed class LocalizedProblemSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
            return Task.CompletedTask;

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Properties["messageKey"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable semantic message identity, translated using messageArguments."
        };
        schema.Properties["messageArguments"] = Arguments();
        if (typeof(HttpValidationProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            schema.Properties["errorMessages"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["key"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["arguments"] = Arguments()
                        }
                    }
                }
            };
        }
        return Task.CompletedTask;
    }

    private static OpenApiSchema Arguments() => new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = true,
        Description = "Named interpolation arguments."
    };
}
