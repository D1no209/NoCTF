using System.Security.Cryptography;
using System.Text;
using DynamicExpresso;

namespace NoCTF.GameModes.Ctf.Scoring;

public sealed record CtfScoreVariables(
    decimal InitialPoints,
    decimal MinimumPoints,
    int SolveCount,
    int EligibleTeamCount,
    decimal DecayParameter);

public sealed class CtfScoreExpression
{
    public const int MaximumExpressionBytes = 4096;
    private const int Capacity = 1024;
    private readonly object sync = new();
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = [];

    public long Evaluate(string expression, CtfScoreVariables variables)
    {
        var compiled = GetOrCompile(expression);
        var raw = (decimal)compiled.Invoke(
            variables.InitialPoints,
            variables.MinimumPoints,
            variables.SolveCount,
            variables.EligibleTeamCount,
            variables.DecayParameter);
        var bounded = decimal.Clamp(raw, variables.MinimumPoints, variables.InitialPoints);
        return checked((long)decimal.Round(bounded, 0, MidpointRounding.AwayFromZero));
    }

    public void Validate(string expression, decimal initialPoints, decimal minimumPoints, decimal decayParameter, int eligibleTeamCount)
    {
        if (initialPoints <= 0
            || minimumPoints < 0
            || minimumPoints > initialPoints
            || decayParameter <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expression), "Score expression boundaries are invalid.");
        }

        foreach (var solveCount in new[] { 0, 1, eligibleTeamCount })
        {
            Evaluate(
                expression,
                new(initialPoints, minimumPoints, solveCount, eligibleTeamCount, decayParameter));
        }
    }

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

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expression)));
        lock (sync)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                recency.Remove(cached.Node);
                recency.AddFirst(cached.Node);
                return cached.Expression;
            }

            var interpreter = new Interpreter(InterpreterOptions.Default)
                .EnableAssignment(AssignmentOperators.None)
                .SetDefaultNumberType(DefaultNumberType.Decimal);
            var compiled = interpreter.Parse(
                expression,
                typeof(decimal),
                new Parameter("initialPoints", typeof(decimal)),
                new Parameter("minimumPoints", typeof(decimal)),
                new Parameter("solveCount", typeof(int)),
                new Parameter("eligibleTeamCount", typeof(int)),
                new Parameter("decayParameter", typeof(decimal)));
            var node = recency.AddFirst(key);
            cache.Add(key, new(compiled, node));
            if (cache.Count > Capacity)
            {
                var oldest = recency.Last!;
                recency.RemoveLast();
                cache.Remove(oldest.Value);
            }
            return compiled;
        }
    }

    private sealed record CacheEntry(Lambda Expression, LinkedListNode<string> Node);
}
