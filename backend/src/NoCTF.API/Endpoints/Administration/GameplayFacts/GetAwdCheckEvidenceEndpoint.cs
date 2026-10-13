using FastEndpoints;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed record AwdCheckEvidenceResponse(Guid GameplayFactId,
    Guid CompetitionChallengeId, Guid TeamId, Guid RoundId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdHealth>))] AwdHealth? Health,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdHealth>))] AwdHealth? KnownTargetFailure,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckExecution>))] AwdCheckExecution Execution,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdSettlementState>))] AwdSettlementState Settlement,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckReason>))] AwdCheckReason Reason,
    AwdCheckProbeResponse Connection, AwdCheckProbeResponse Functionality,
    DateTimeOffset? ScheduledAt, DateTimeOffset? Deadline, DateTimeOffset? ReceiptDeadline,
    DateTimeOffset? ObservedAt, DateTimeOffset? ReceivedAt, DateTimeOffset? AdjudicatedAt,
    Guid? AttemptId, DateTimeOffset? AttemptStartedAt, DateTimeOffset? AttemptDeadline, int Attempt,
    bool FinalScheduledCheck, bool ManifestComplete, bool EvidenceComplete,
    int RequiredFlags, int ReadableFlags, IReadOnlyList<AwdCheckFlagEvidenceResponse> Flags)
{
    public static AwdCheckEvidenceResponse From(AwdCheckEvidenceView view)
    {
        var e = view.Evidence;
        return new(view.GameplayFactId, view.CompetitionChallengeId, view.TeamId,
            view.RoundId, e.Health, e.KnownTargetFailure, e.Execution, e.Settlement, e.Reason,
            new(e.Connection, e.ConnectionReason), new(e.Functionality, e.FunctionalityReason),
            e.ScheduledAt, e.Deadline, e.ReceiptDeadline, e.ObservedAt, e.ReceivedAt, e.AdjudicatedAt,
            e.AttemptId, e.AttemptStartedAt, e.AttemptDeadline, e.Attempt, e.FinalScheduledCheck,
            e.ManifestComplete, e.EvidenceComplete, e.RequiredFlags, e.ReadableFlags,
            e.Flags.Select(flag => new AwdCheckFlagEvidenceResponse(flag.FlagId, flag.Historical,
                flag.RequiresWrite, flag.Obligation, new(flag.Write, flag.WriteReason),
                new(flag.Read, flag.ReadReason))).ToArray());
    }
}

public sealed record AwdCheckProbeResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdProbeOutcome>))] AwdProbeOutcome Outcome,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckReason>))] AwdCheckReason Reason);
public sealed record AwdCheckFlagEvidenceResponse(Guid FlagId, bool Historical, bool RequiresWrite,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdFlagObligation>))] AwdFlagObligation Obligation,
    AwdCheckProbeResponse Write, AwdCheckProbeResponse Read);

public sealed class GetAwdCheckEvidenceEndpoint(IAwdCheckAdjudicationStore store,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<AwdCheckEvidenceResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/awd/checks/{gameplayFactId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetAwdCheckEvidence"));
        Summary(summary => summary.Summary = "Reads AWD check evidence without flag values or private read credentials.");
    }

    public override async Task<Results<Ok<AwdCheckEvidenceResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var view = await store.ReadAsync(competitionId, Route<Guid>("gameplayFactId"), ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(AwdCheckEvidenceResponse.From(view));
    }
}
