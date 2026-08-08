using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionSubjectCode>))]
public enum CompetitionQuestionSubjectCode
{
    Challenge,
    Platform
}
[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionStatusCode>))]
public enum CompetitionQuestionStatusCode
{
    Pending,
    Replied,
    Resolved,
    Closed
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionAccessCode>))]
public enum CompetitionQuestionAccessCode
{
    Asker,
    Observer,
    Handler
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionEntryKindCode>))]
public enum CompetitionQuestionEntryKindCode
{
    Message,
    StatusTransition
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionQuestionParticipantRoleCode>))]
public enum CompetitionQuestionParticipantRoleCode
{
    Asker,
    Handler
}

public sealed record CompetitionQuestionEntryResponse(
    Guid Id,
    CompetitionQuestionEntryKindCode Kind,
    CompetitionQuestionParticipantRoleCode ActorRole,
    Guid? ActorUserId,
    string ActorDisplayName,
    string? Body,
    CompetitionQuestionStatusCode? FromStatus,
    CompetitionQuestionStatusCode? ToStatus,
    Guid? TargetEntryId,
    DateTimeOffset CreatedAt);

public sealed record CompetitionQuestionResponse(
    Guid Id,
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    Guid? AskedByUserId,
    string AskerDisplayName,
    string? TeamDisplayName,
    Guid? SubmissionId,
    CompetitionQuestionSubjectCode Subject,
    string Title,
    string Body,
    CompetitionQuestionStatusCode Status,
    CompetitionQuestionAccessCode Access,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanReply,
    bool CanResolve,
    bool CanClose,
    IReadOnlyList<CompetitionQuestionEntryResponse> Entries);

internal static class CompetitionQuestionResponseMapper
{
    public static CompetitionQuestionResponse ToResponse(CompetitionQuestionView source)
    {
        var asker = source.Access == CompetitionQuestionAccess.Asker;
        var handler = source.Access == CompetitionQuestionAccess.Handler;
        return new(
            source.Id,
            source.CompetitionId,
            source.CompetitionChallengeId,
            source.TeamId,
            source.AskedByUserId,
            source.AskerDisplayName,
            source.TeamDisplayName,
            source.SubmissionId,
            ToSubject(source.Subject),
            source.Title,
            source.Body,
            ToStatus(source.Status),
            (CompetitionQuestionAccessCode)source.Access,
            source.Revision,
            source.CreatedAt,
            source.UpdatedAt,
            CanReply: asker && source.Status != CompetitionQuestionStatus.Closed
                || handler && source.Status is CompetitionQuestionStatus.Pending
                    or CompetitionQuestionStatus.Replied,
            CanResolve: asker && source.Status == CompetitionQuestionStatus.Replied
                || handler && source.Status is CompetitionQuestionStatus.Pending
                    or CompetitionQuestionStatus.Replied,
            CanClose: handler && source.Status != CompetitionQuestionStatus.Closed,
            source.Entries.Select(ToResponse).ToArray());
    }

    public static CompetitionQuestionSubject ToDomain(CompetitionQuestionSubjectCode value) =>
        (CompetitionQuestionSubject)value;

    public static CompetitionQuestionStatus ToDomain(CompetitionQuestionStatusCode value) =>
        (CompetitionQuestionStatus)value;

    private static CompetitionQuestionEntryResponse ToResponse(
        CompetitionQuestionEntryView source) =>
        new(
            source.Id,
            (CompetitionQuestionEntryKindCode)source.Kind,
            (CompetitionQuestionParticipantRoleCode)source.ActorRole,
            source.ActorUserId,
            source.ActorDisplayName,
            source.Body,
            source.FromStatus is null ? null : ToStatus(source.FromStatus.Value),
            source.ToStatus is null ? null : ToStatus(source.ToStatus.Value),
            source.TargetEntryId,
            source.CreatedAt);

    private static CompetitionQuestionSubjectCode ToSubject(
        CompetitionQuestionSubject value) =>
        (CompetitionQuestionSubjectCode)value;

    private static CompetitionQuestionStatusCode ToStatus(
        CompetitionQuestionStatus value) =>
        (CompetitionQuestionStatusCode)value;
}

public sealed class GetCompetitionQuestionEndpoint(
    GetCompetitionQuestion get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionQuestionResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/questions/{questionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetCompetitionQuestion"));
        Summary(summary =>
        {
            summary.Summary = "Returns one accessible competition question.";
            summary.Description = "Private content is projected only to the asker, read-only observers, competition handlers, and the linked challenge authors.";
        });
    }

    public override async Task<Results<Ok<CompetitionQuestionResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(CancellationToken ct)
    {
        if (!user.IsHuman)
            return TypedResults.Forbid();
        var question = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("questionId"),
            user.UserId,
            ct);
        return question is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionQuestionResponseMapper.ToResponse(question));
    }
}
