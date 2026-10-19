using System.Collections.Concurrent;
using System.Text;
using DynamicExpresso;

namespace NoCTF.GameModes.Scoring;

public sealed record ScoreCurveVariables(
    decimal InitialPoints,
    decimal MinimumPoints,
    int SolveCount,
    int EligibleTeamCount,
    decimal DecayTeamCount);

public sealed class ScoreCurveExpression
{
    public const int MaximumExpressionBytes = 4096;
    private const int Capacity = 1024;
    // Lock-free: readers never block, and each distinct expression is compiled once.
    private readonly ConcurrentDictionary<string, Lazy<Lambda>> cache = new(StringComparer.Ordinal);

    public decimal Evaluate(string expression, ScoreCurveVariables variables)
    {
        var compiled = GetOrCompile(expression);
        var raw = (decimal)compiled.Invoke(
            variables.InitialPoints,
            variables.MinimumPoints,
            variables.SolveCount,
            variables.EligibleTeamCount,
            variables.DecayTeamCount);
        return raw;
    }

    public void Validate(string expression, decimal initialPoints, decimal minimumPoints, decimal decayTeamCount, int eligibleTeamCount)
    {
        if (initialPoints <= 0
            || minimumPoints < 0
            || minimumPoints > initialPoints
            || decayTeamCount <= 1
            || eligibleTeamCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expression), "Score expression boundaries are invalid.");
        }

        for (var solveCount = 0; ; solveCount++)
        {
            Evaluate(
                expression,
                new(initialPoints, minimumPoints, solveCount, eligibleTeamCount, decayTeamCount));
            if (solveCount == eligibleTeamCount)
            {
                break;
            }
        }
    }

    public void ValidateSyntax(string expression) => _ = GetOrCompile(expression);

    public static bool IsValidationException(Exception exception) =>
        exception is DynamicExpresso.Exceptions.DynamicExpressoException
            or ArgumentException
            or InvalidOperationException
            or OverflowException
            or DivideByZeroException;

    private Lambda GetOrCompile(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var byteLength = Encoding.UTF8.GetByteCount(expression);
        if (byteLength is < 1 or > MaximumExpressionBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expression),
                $"Expression must contain 1..{MaximumExpressionBytes} UTF-8 bytes.");
        }

        if (cache.TryGetValue(expression, out var cached))
            return cached.Value;
        // Competitions use a handful of curves; the bound only guards against unbounded validation input.
        if (cache.Count >= Capacity)
            cache.Clear();
        var entry = cache.GetOrAdd(expression, static value => new Lazy<Lambda>(() => Compile(value)));
        try
        {
            return entry.Value;
        }
        catch
        {
            // Invalid expressions are not retained.
            cache.TryRemove(new KeyValuePair<string, Lazy<Lambda>>(expression, entry));
            throw;
        }
    }

    private static Lambda Compile(string expression) =>
        new Interpreter(InterpreterOptions.Default)
            .EnableAssignment(AssignmentOperators.None)
            .SetDefaultNumberType(DefaultNumberType.Decimal)
            .Parse(
                expression,
                typeof(decimal),
                new Parameter("initialPoints", typeof(decimal)),
                new Parameter("minimumPoints", typeof(decimal)),
                new Parameter("solveCount", typeof(int)),
                new Parameter("eligibleTeamCount", typeof(int)),
                new Parameter("decayTeamCount", typeof(decimal)));
}
