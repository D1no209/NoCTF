using Microsoft.Extensions.Options;
using NoCTF.Application.Admission;

namespace NoCTF.Infrastructure.Admission;

public sealed class CredentialWorkAdmission(IRequestAdmission admission, IOptions<RequestAdmissionOptions> options) : ICredentialWorkAdmission
{
    public ValueTask<IRequestAdmissionLease> AcquireAsync(string accountKey, CancellationToken ct) =>
        admission.AcquireAsync([new($"credential-account:{accountKey}", options.Value.AuthenticationAccountPerMinute, 60)],
            [new("password-computation", options.Value.PasswordConcurrency)], ct);
}
