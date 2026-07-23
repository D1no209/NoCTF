using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ListDeadLettersRequest
{
    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class ListDeadLettersValidator : Validator<ListDeadLettersRequest>
{
    public ListDeadLettersValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record DeadLetterResponse(
    Guid MessageId,
    string MessageType,
    string Source,
    string ExceptionType,
    DateTimeOffset SentAt,
    bool Replayable);

public sealed record DeadLetterListResponse(IReadOnlyList<DeadLetterResponse> Items);

internal static class DeadLetterMapping
{
    public static DeadLetterResponse ToResponse(DeadLetterView view) =>
        new(
            view.MessageId, view.MessageType, view.Source, view.ExceptionType,
            view.SentAt, view.Replayable);
}

public sealed class ListDeadLettersEndpoint(ManagePlatform platform)
    : Endpoint<ListDeadLettersRequest, Ok<DeadLetterListResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/dead-letters");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Summary(summary =>
        {
            summary.Summary = "Lists redacted Wolverine dead letters.";
            summary.Description = "Message bodies and exception messages are deliberately omitted.";
        });
    }

    public override async Task<Ok<DeadLetterListResponse>> ExecuteAsync(
        ListDeadLettersRequest request,
        CancellationToken ct) =>
        TypedResults.Ok(new DeadLetterListResponse(
            (await platform.ListDeadLettersAsync(request.Limit, ct))
                .Select(DeadLetterMapping.ToResponse)
                .ToArray()));
}
