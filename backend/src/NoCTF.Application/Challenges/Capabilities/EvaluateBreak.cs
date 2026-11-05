using System.Text;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Common;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public sealed record BreakCandidate(Guid Id, string Value, byte[] Digest, ChallengeFlagMatchKind MatchKind, Guid? OwnerTeamId = null)
{
    public override string ToString() => $"BreakCandidate {{ Id = {Id}, MatchKind = {MatchKind}, Value = [REDACTED] }}";
}

public sealed record BreakInput(Guid ExecutionId, string Flag, IReadOnlyList<BreakCandidate> Candidates, DateTimeOffset ObservedAt,
    CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact)
{
    public override string ToString() => $"BreakInput {{ ExecutionId = {ExecutionId}, Scope = {Scope}, CandidateCount = {Candidates.Count}, Flag = [REDACTED] }}";
}

public sealed class EvaluateBreak
{
    public OperationResult<BreakEvaluation, CapabilityExecutionFailure> Execute(BreakInput input)
    {
        if (input.ExecutionId == Guid.Empty || !Enum.IsDefined(input.Scope) || Encoding.UTF8.GetByteCount(input.Flag) is < 1 or > 4096 || input.Flag.Contains('\0'))
            return OperationResult<BreakEvaluation, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidInput, "An execution identity and a Flag of 1 to 4096 UTF-8 bytes are required.");
        if (input.Candidates.Any(flag => flag.Id == Guid.Empty)
            || input.Candidates.Select(flag => flag.Id).Distinct().Count() != input.Candidates.Count)
            throw new ArgumentException("The candidate set must contain distinct persisted Flag identities.", nameof(input));
        if (input.Candidates.Any(candidate => !Enum.IsDefined(candidate.MatchKind)
            || candidate.MatchKind == ChallengeFlagMatchKind.Exact && candidate.Digest.Length != 32
            || candidate.MatchKind == ChallengeFlagMatchKind.RegularExpression && !ChallengeFlagMatcher.IsValidRegularExpression(candidate.Value)))
            return OperationResult<BreakEvaluation, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidEvidence, "A Flag candidate has invalid matching data.");
        var matches = input.Candidates.Where(candidate => ChallengeFlagMatcher.IsMatch(input.Flag, candidate.MatchKind, candidate.Value, candidate.Digest))
            .Select(candidate => new BreakMatchEvidence(candidate.Id, candidate.OwnerTeamId)).ToList();
        return OperationResult<BreakEvaluation, CapabilityExecutionFailure>.Success(new()
        {
            ExecutionId = input.ExecutionId, Scope = input.Scope, ObservedAt = input.ObservedAt, Flags = matches,
            Match = matches.Count switch { 0 => BreakMatchResult.NotMatched, 1 => BreakMatchResult.Matched, _ => BreakMatchResult.Ambiguous }
        });
    }
}
