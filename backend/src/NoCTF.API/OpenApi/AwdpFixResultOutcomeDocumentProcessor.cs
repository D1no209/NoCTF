using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace NoCTF.API.OpenApi;

internal sealed class AwdpFixResultOutcomeDocumentProcessor : IDocumentProcessor
{
    private const string SchemaName = "NoCTFAPIEndpointsInternalAwdpFixResultOutcome";

    public void Process(DocumentProcessorContext context)
    {
        if (!context.Document.Components.Schemas.TryGetValue(SchemaName, out var schema))
            return;

        schema.Type = JsonObjectType.String;
        schema.Format = null;
        schema.Enumeration.Clear();
        schema.EnumerationNames.Clear();
        foreach (var outcome in new[]
        {
            "ExploitSucceeded",
            "DefenseSucceeded",
            "ServiceAbnormal"
        })
        {
            schema.Enumeration.Add(outcome);
            schema.EnumerationNames.Add(outcome);
        }
    }
}
