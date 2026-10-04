using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Reflection;

namespace NoCTF.API.OpenApi;

/// <summary>Normalizes FE 8.2 nullable references and its orphaned operation-local form-file references.</summary>
public sealed class OpenApiReferenceDocumentTransformer : IOpenApiDocumentTransformer
{
    private static readonly PropertyInfo[] SchemaProperties = typeof(OpenApiSchema).GetProperties()
        .Where(property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true
            && property.Name != nameof(OpenApiSchema.Const)).ToArray();

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        if (document.Components?.Schemas is { } schemas)
            foreach (var (name, schema) in schemas.ToArray())
                schemas[name] = Normalize(schema, document);
        return Task.CompletedTask;
    }

    private static IOpenApiSchema Normalize(IOpenApiSchema value, OpenApiDocument document)
    {
        // FE 8.2 removes the IFormFile component after producing operation-local references to it.
        if (value is OpenApiSchemaReference reference
            && reference.Reference.Id is { } id
            && id.StartsWith("IFormFile__op", StringComparison.Ordinal)
            && document.Components?.Schemas?.ContainsKey(id) != true)
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        if (value is not OpenApiSchema schema) return value;
        // FE 8.2's fallback clone assigns every public property, including an absent Const.
        // OpenAPI.NET 2.12 distinguishes absent Const from explicit null; the latter emits enum:[null].
        // Copy public schema state without assigning an absent constant. Do not alter actual enums.
        var normalized = new OpenApiSchema();
        foreach (var property in SchemaProperties)
            property.SetValue(normalized, property.GetValue(schema));
        if (schema.Const is not null) normalized.Const = schema.Const;
        schema = normalized;
        if (schema.OneOf is { Count: 1 } && schema.OneOf[0] is OpenApiSchemaReference
            && schema.Type is { } type && type.HasFlag(JsonSchemaType.Null))
        {
            schema.Type = JsonSchemaType.Null;
        }
        if (schema.Properties is not null)
            foreach (var (name, property) in schema.Properties.ToArray())
                schema.Properties[name] = Normalize(property, document);
        if (schema.Items is not null) schema.Items = Normalize(schema.Items, document);
        if (schema.AdditionalProperties is not null) schema.AdditionalProperties = Normalize(schema.AdditionalProperties, document);
        if (schema.OneOf is not null)
            for (var index = 0; index < schema.OneOf.Count; index++) schema.OneOf[index] = Normalize(schema.OneOf[index], document);
        if (schema.AnyOf is not null)
            for (var index = 0; index < schema.AnyOf.Count; index++) schema.AnyOf[index] = Normalize(schema.AnyOf[index], document);
        if (schema.AllOf is not null)
            for (var index = 0; index < schema.AllOf.Count; index++) schema.AllOf[index] = Normalize(schema.AllOf[index], document);
        return schema;
    }
}
