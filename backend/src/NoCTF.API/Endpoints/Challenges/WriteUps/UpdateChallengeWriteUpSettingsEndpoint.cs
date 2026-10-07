using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class UpdateChallengeWriteUpSettingsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public bool? Enabled { get; set; }
    public int? DeadlineHours { get; set; }
    private int? deductionPercent;
    public int? DeductionPercent { get => deductionPercent; set { deductionPercent = value; DeductionSpecified = true; } }
    [JsonIgnore] public bool DeductionSpecified { get; private set; }
}
public sealed class UpdateChallengeWriteUpSettingsValidator : Validator<UpdateChallengeWriteUpSettingsRequest>
{
    public UpdateChallengeWriteUpSettingsValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
        RuleFor(x => x.DeductionPercent).InclusiveBetween(0, 100).When(x => x.DeductionPercent is not null);
        RuleFor(x => x.DeadlineHours).InclusiveBetween(0, ChallengeWriteUpPolicy.MaximumDeadlineHours).When(x => x.DeadlineHours is not null);
        RuleFor(x => x).Must(x => x.Enabled is not null || x.DeadlineHours is not null || x.DeductionSpecified);
    }
}
public sealed class UpdateChallengeWriteUpSettingsEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<UpdateChallengeWriteUpSettingsRequest, Results<Ok<ChallengeWriteUpSettingsResponse>, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Patch("/admin/competitions/{competitionId}/writeup-settings"); AuthSchemes("Bearer");
        Description(x => x.WithName("UpdateChallengeWriteUpSettings"));
        Summary(x => x.Summary = "Updates the competition policy or nullable per-challenge override; omitted fields preserve current values.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpSettingsResponse>, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>>> ExecuteAsync(UpdateChallengeWriteUpSettingsRequest request, CancellationToken ct)
    {
        var result = await writeUps.UpdateSettingsAsync(new(request.CompetitionId, request.CompetitionChallengeId, user.UserId,
            request.ExpectedStamp, request.Enabled, request.DeductionPercent, request.DeadlineHours, request.DeductionSpecified, clock.GetUtcNow()), ct);
        if (result.Failure == ChallengeWriteUpFailure.Forbidden) return TypedResults.Forbid();
        if (result.Failure is { } failure) return TypedResults.Conflict(new ChallengeWriteUpFailureResponse(failure));
        return TypedResults.Ok(new ChallengeWriteUpSettingsResponse(result.Settings!, result.ConcurrencyStamp!.Value));
    }
}
