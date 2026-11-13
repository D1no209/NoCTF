using System.Security.Cryptography;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Credentials;

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
            .WithMessage("A non-empty CSV or XLSX file up to 10 MB is required.");
    }
}

public sealed record CompetitionCredentialImportIssueResponse(
    string Code,
    int RowNumber,
    string Message);

public sealed record ImportCompetitionCredentialsResponse(
    Guid? BatchId,
    int CreatedUsers,
    int CreatedTeams,
    int CreatedCredentials,
    IReadOnlyList<CompetitionCredentialImportIssueResponse> Issues);

public sealed class ImportCompetitionCredentialsEndpoint(
    ICompetitionCredentialImportReader reader,
    ImportCompetitionCredentials import,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<ImportCompetitionCredentialsRequest,
        Results<Created<ImportCompetitionCredentialsResponse>, BadRequest<ImportCompetitionCredentialsResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/credential-batches/import");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("AdminImportCompetitionCredentials"));
    }

    public override async Task<Results<Created<ImportCompetitionCredentialsResponse>, BadRequest<ImportCompetitionCredentialsResponse>, ProblemHttpResult>> ExecuteAsync(
        ImportCompetitionCredentialsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
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
                    issue.Code.ToString(), issue.RowNumber, issue.Message)).ToArray()));

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
                issue.Code.ToString(), issue.RowNumber, issue.Message)).ToArray());
        if (result.Failure == CompetitionCredentialImportFailure.None)
            return TypedResults.Created($"/admin/competitions/{competitionId}/credential-batches/{result.BatchId}", response);
        return TypedResults.BadRequest(response);
    }
}
