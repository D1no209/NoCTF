using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace NoCTF.API.OpenApi;

internal sealed class EndpointMetadataDocumentProcessor : IDocumentProcessor
{
    public void Process(DocumentProcessorContext context)
    {
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, pathItem) in context.Document.Paths)
        {
            foreach (var (method, operation) in pathItem)
            {
                if (string.IsNullOrWhiteSpace(operation.OperationId))
                    throw new InvalidOperationException($"OpenAPI operation {method} {path} has no OperationId.");
                if (!operationIds.Add(operation.OperationId))
                    throw new InvalidOperationException(
                        $"OpenAPI OperationId '{operation.OperationId}' is not unique.");

                if (string.IsNullOrWhiteSpace(operation.Summary))
                    operation.Summary = Humanize(operation.OperationId);
                if (string.IsNullOrWhiteSpace(operation.Description))
                    operation.Description = $"Executes {method.ToString().ToUpperInvariant()} {path}.";
            }
        }
    }

    private static string Humanize(string operationId)
    {
        var words = new List<char>(operationId.Length + 8);
        for (var index = 0; index < operationId.Length; index++)
        {
            var current = operationId[index];
            if (index > 0 && char.IsUpper(current) && char.IsLower(operationId[index - 1]))
                words.Add(' ');
            words.Add(current == '_' ? ' ' : current);
        }
        return new string(words.ToArray());
    }
}
