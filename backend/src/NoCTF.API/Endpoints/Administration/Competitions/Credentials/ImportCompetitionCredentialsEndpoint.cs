using System.Security.Cryptography;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Credentials;
using NoCTF.Application.Teams.Moderation;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed class ImportCompetitionCredentialsRequest
{
    public IFormFile? File { get; set; }
}

public sealed class ImportCompetitionCredentialsValidator
    : Validator<ImportCompetitionCredentialsRequest>
{
    public ImportCompetitionCredentialsValidator()
    {
        RuleFor(request => request.File)
            .Must(file => file is { Length: > 0 and <= 10_000_000 })
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.CredentialImportFileRequired))
            .WithErrorCode(ApiMessages.Key(ApiMessageId.CredentialImportFileRequired));
    }
}

public sealed record CompetitionCredentialImportIssueResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionCredentialImportIssueCode>))]
    CompetitionCredentialImportIssueCode Code,
    int RowNumber,
    string Message);

public sealed record ImportCompetitionCredentialsResponse(
    Guid? BatchId,
    int CreatedUsers,
    int CreatedTeams,
    int CreatedCredentials,
    IReadOnlyList<CompetitionCredentialImportIssueResponse> Issues,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionCredentialImportFailure>))]
    CompetitionCredentialImportFailure Code)
{
    public string Message => Code == CompetitionCredentialImportFailure.None ? string.Empty : ApiMessages.Text(Code switch
    {
        CompetitionCredentialImportFailure.TransactionConflict => ApiMessageId.CredentialImportConflict,
        CompetitionCredentialImportFailure.EmptyFile => ApiMessageId.CredentialImportEmpty,
        CompetitionCredentialImportFailure.CompetitionFinished => ApiMessageId.CredentialImportFinished,
        _ => ApiMessageId.CredentialImportRejected
    });
}

public sealed class ImportCompetitionCredentialsEndpoint(
    ICompetitionCredentialImportReader reader,
    ImportCompetitionCredentials import,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<ImportCompetitionCredentialsRequest,
        Results<Created<ImportCompetitionCredentialsResponse>, BadRequest<ImportCompetitionCredentialsResponse>, ForbidHttpResult, NotFound, Conflict<ImportCompetitionCredentialsResponse>>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/credential-batches/import");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("AdminImportCompetitionCredentials"));
    }

    public override async Task<Results<Created<ImportCompetitionCredentialsResponse>, BadRequest<ImportCompetitionCredentialsResponse>, ForbidHttpResult, NotFound, Conflict<ImportCompetitionCredentialsResponse>>> ExecuteAsync(
        ImportCompetitionCredentialsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        await using var input = request.File!.OpenReadStream();
        await using var content = new MemoryStream();
        await input.CopyToAsync(content, ct);
        var bytes = content.ToArray();
        content.Position = 0;
        var read = await reader.ReadAsync(content, request.File.FileName, ct);
        if (read.Issues.Count > 0)
            return TypedResults.BadRequest(new ImportCompetitionCredentialsResponse(
                null,
                0,
                0,
                0,
                read.Issues.Select(issue => new CompetitionCredentialImportIssueResponse(
                    issue.Code, issue.RowNumber, issue.Message)).ToArray(), CompetitionCredentialImportFailure.InvalidRows));

        var result = await import.ExecuteAsync(new(
            competitionId,
            user.UserId,
            request.File.FileName,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            read.Rows,
            clock.GetUtcNow()), ct);
        var response = new ImportCompetitionCredentialsResponse(
            result.BatchId,
            result.CreatedUsers,
            result.CreatedTeams,
            result.CreatedCredentials,
            result.Issues.Select(issue => new CompetitionCredentialImportIssueResponse(
                issue.Code, issue.RowNumber, issue.Message)).ToArray(), result.Failure);
        if (result.Failure == CompetitionCredentialImportFailure.Forbidden)
            return TypedResults.Forbid();
        if (result.Failure == CompetitionCredentialImportFailure.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.Failure == CompetitionCredentialImportFailure.TransactionConflict)
            return TypedResults.Conflict(response);
        if (result.Failure == CompetitionCredentialImportFailure.None)
            return TypedResults.Created($"/api/v1/admin/competitions/{competitionId}/credential-batches/{result.BatchId}", response);
        return TypedResults.BadRequest(response);
    }
}
