using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.API.Security;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class BeginPasskeyLoginRequest { public string ReturnPath { get; set; } = "/"; }
public sealed class BeginPasskeyLoginValidator : Validator<BeginPasskeyLoginRequest>
{
    public BeginPasskeyLoginValidator() => RuleFor(value => value.ReturnPath).NotEmpty().MaximumLength(2048);
}
public sealed record PasskeyOptionsResponse(JsonElement PublicKey, DateTimeOffset ExpiresAt)
{
    public static PasskeyOptionsResponse From(PasskeyStartedCeremony value)
    { using var document = JsonDocument.Parse(value.OptionsJson); return new(document.RootElement.Clone(), value.ExpiresAt); }
}
public sealed class BeginPasskeyLoginEndpoint(IPasskeyStore store, PasskeyBrowserFlow browser)
    : Endpoint<BeginPasskeyLoginRequest, Results<Ok<PasskeyOptionsResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/passkeys/login/options"); AllowAnonymous(); MaxRequestBodySize(4096);
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.Authentication)));
    }
    public override async Task<Results<Ok<PasskeyOptionsResponse>, ProblemHttpResult>> ExecuteAsync(BeginPasskeyLoginRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var origin = PasskeyBrowserFlow.Origin(HttpContext.Request);
        if (origin is null) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        var result = await store.BeginLoginAsync(origin, request.ReturnPath, ct);
        if (!result.Succeeded) return PasskeyEndpointResults.Failure(result.FailureCode!.Value);
        browser.Write(HttpContext, result.Value!); return TypedResults.Ok(PasskeyOptionsResponse.From(result.Value!));
    }
}
