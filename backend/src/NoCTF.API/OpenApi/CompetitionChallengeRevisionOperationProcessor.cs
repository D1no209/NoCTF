using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace NoCTF.API.OpenApi;

internal sealed class CompetitionChallengeRevisionOperationProcessor : IOperationProcessor
{
    private static readonly HashSet<string> OperationIds =
    [
        "AdminDeleteCompetitionChallenge",
        "AdminRestoreCompetitionChallenge"
    ];

    public bool Process(OperationProcessorContext context)
    {
        var operation = context.OperationDescription.Operation;
        if (!OperationIds.Contains(operation.OperationId))
            return true;

        var expectedRevision = operation.Parameters.SingleOrDefault(parameter =>
            parameter.Kind == OpenApiParameterKind.Query
            && string.Equals(
                parameter.Name,
                "expectedRevision",
                StringComparison.Ordinal));
        if (expectedRevision is null)
        {
            throw new InvalidOperationException(
                $"{operation.OperationId} must expose expectedRevision as a query parameter.");
        }

        expectedRevision.IsRequired = true;
        expectedRevision.ActualSchema.Minimum = 0;
        expectedRevision.ActualSchema.IsNullableRaw = false;
        return true;
    }
}
