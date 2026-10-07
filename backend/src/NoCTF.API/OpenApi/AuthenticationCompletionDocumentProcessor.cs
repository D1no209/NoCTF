using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace NoCTF.API.OpenApi;

// STJ writes the discriminator; expose the same closed union to generated clients.
internal sealed class AuthenticationCompletionDocumentProcessor : IDocumentProcessor
{
    public void Process(DocumentProcessorContext context)
    {
        const string prefix = "NoCTFAPIEndpointsAuthentication";
        if (!context.Document.Components.Schemas.TryGetValue(prefix + "AuthenticationResponse", out var root)) return;
        root.Properties.Clear();
        root.RequiredProperties.Clear();
        root.OneOf.Clear();
        foreach (var state in new[] { "Authenticated", "MfaRequired", "EnrollmentRequired" })
        {
            var schema = context.Document.Components.Schemas[prefix + state + "Response"];
            foreach (var inherited in schema.AllOf.Where(value => value.Reference is null).ToArray())
            {
                foreach (var property in inherited.Properties) schema.Properties[property.Key] = property.Value;
                foreach (var required in inherited.RequiredProperties) schema.RequiredProperties.Add(required);
            }
            schema.AllOf.Clear();
            schema.Type = JsonObjectType.Object;
            schema.Properties["state"] = new JsonSchemaProperty { Type = JsonObjectType.String, Enumeration = { state } };
            schema.RequiredProperties.Add("state");
            foreach (var property in schema.Properties.Keys.Where(value => value != "recoveryCodes")) schema.RequiredProperties.Add(property);
            root.OneOf.Add(new JsonSchema { Reference = schema });
        }
        root.Type = JsonObjectType.None;
    }
}
