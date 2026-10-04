using Microsoft.AspNetCore.Mvc;
using NJsonSchema;
using NJsonSchema.Generation;

namespace NoCTF.API.OpenApi;

public sealed class LocalizedProblemSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.ContextualType.Type)) return;
        context.Schema.Properties["messageKey"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.String,
            Description = "Stable semantic message identity. Translate with messageArguments; never infer identity from detail text."
        };
        context.Schema.Properties["messageArguments"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.Object,
            AllowAdditionalProperties = true,
            Description = "Named interpolation arguments."
        };
        if (!typeof(ValidationProblemDetails).IsAssignableFrom(context.ContextualType.Type)) return;
        var descriptor = new JsonSchema { Type = JsonObjectType.Object };
        descriptor.Properties["key"] = new JsonSchemaProperty { Type = JsonObjectType.String };
        descriptor.Properties["arguments"] = new JsonSchemaProperty { Type = JsonObjectType.Object, AllowAdditionalProperties = true };
        context.Schema.Properties["errorMessages"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.Object,
            AdditionalPropertiesSchema = new JsonSchema { Type = JsonObjectType.Array, Item = descriptor },
            Description = "Per-field message descriptors, in the same order as errors."
        };
    }
}
