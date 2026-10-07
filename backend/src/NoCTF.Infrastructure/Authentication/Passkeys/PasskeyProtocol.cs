using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Passkeys;

namespace NoCTF.Infrastructure.Authentication.Passkeys;

public sealed class PasskeyProtocol(IPasskeyHandler<User> handler) : IPasskeyProtocol
{
    public async Task<PasskeyProtocolOptions> CreateRegistrationOptionsAsync(PasskeyUserIdentity user, string origin, CancellationToken ct)
    {
        var value = await handler.MakeCreationOptionsAsync(new() { Id = user.Id.ToString("N"), Name = user.Name, DisplayName = user.Name }, Context(origin, ct));
        return new(value.CreationOptionsJson, value.AttestationState!);
    }
    public async Task<PasskeyProtocolOptions> CreateLoginOptionsAsync(string origin, CancellationToken ct)
    {
        var value = await handler.MakeRequestOptionsAsync(null, Context(origin, ct));
        return new(value.RequestOptionsJson, value.AssertionState!);
    }
    public async Task<OperationResult<PasskeyAttestation, PasskeyFailure>> VerifyRegistrationAsync(string state, string json, string origin, CancellationToken ct)
    {
        var result = await handler.PerformAttestationAsync(new() { HttpContext = Context(origin, ct), AttestationState = state, CredentialJson = json });
        return result.Succeeded && Guid.TryParse(result.UserEntity!.Id, out var userId)
            ? OperationResult<PasskeyAttestation, PasskeyFailure>.Success(new(userId, Credential(result.Passkey!))) : Invalid<PasskeyAttestation>();
    }
    public async Task<OperationResult<PasskeyAssertion, PasskeyFailure>> VerifyLoginAsync(string state, string json, string origin, CancellationToken ct)
    {
        var result = await handler.PerformAssertionAsync(new() { HttpContext = Context(origin, ct), AssertionState = state, CredentialJson = json });
        return result.Succeeded && result.User is not null && result.User.Kind == UserKind.Human && result.User.AccountStatus == UserAccountStatus.Active
            ? OperationResult<PasskeyAssertion, PasskeyFailure>.Success(new(result.User.Id, Credential(result.Passkey!))) : Invalid<PasskeyAssertion>();
    }
    private static DefaultHttpContext Context(string origin, CancellationToken ct)
    {
        var uri = new Uri(origin); var context = new DefaultHttpContext { RequestAborted = ct };
        context.Request.Scheme = uri.Scheme; context.Request.Host = new HostString(uri.Authority); context.Request.Headers.Origin = origin;
        return context;
    }
    private static VerifiedPasskey Credential(UserPasskeyInfo value) => new(value.CredentialId, value.PublicKey, value.SignCount,
        value.IsUserVerified, value.IsBackupEligible, value.IsBackedUp, value.AttestationObject, value.ClientDataJson,
        (value.Transports ?? []).Select(ParseTransport).Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray());
    private static OperationResult<T, PasskeyFailure> Invalid<T>() => OperationResult<T, PasskeyFailure>.Failure(PasskeyFailure.InvalidCredential, "Passkey verification failed.");
    private static PasskeyTransport? ParseTransport(string value) => value switch
    {
        "usb" => PasskeyTransport.Usb, "nfc" => PasskeyTransport.Nfc, "ble" => PasskeyTransport.Ble,
        "smart-card" => PasskeyTransport.SmartCard, "hybrid" => PasskeyTransport.Hybrid, "internal" => PasskeyTransport.Internal, _ => null
    };
    internal static string TransportText(PasskeyTransport value) => value switch
    {
        PasskeyTransport.Usb => "usb", PasskeyTransport.Nfc => "nfc", PasskeyTransport.Ble => "ble",
        PasskeyTransport.SmartCard => "smart-card", PasskeyTransport.Hybrid => "hybrid", PasskeyTransport.Internal => "internal", _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
