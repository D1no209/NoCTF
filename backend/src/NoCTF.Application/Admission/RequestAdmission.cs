namespace NoCTF.Application.Admission;

public sealed record RateQuota(string Key, int Limit, int WindowSeconds);
public sealed record ConcurrentQuota(string Key, int Limit);
public enum AdmissionFailure { RateLimited, CapacityBusy, DependencyUnavailable }
public sealed class AdmissionRejectedException(AdmissionFailure failure, int retryAfterSeconds = 1)
    : Exception("Request admission was rejected.")
{
    public AdmissionFailure Failure { get; } = failure;
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
public interface IRequestAdmissionLease : IAsyncDisposable
{
    CancellationToken Token { get; }
}
public interface IRequestAdmission
{
    ValueTask<IRequestAdmissionLease> AcquireAsync(IReadOnlyList<RateQuota> rates,
        IReadOnlyList<ConcurrentQuota> concurrency, CancellationToken ct);
}

public interface ICredentialWorkAdmission
{
    ValueTask<IRequestAdmissionLease> AcquireAsync(string accountKey, CancellationToken ct);
}

public sealed class RequestAdmissionOptions
{
    public int AuthenticationIpPerMinute { get; set; } = 600;
    public int AuthenticationAccountPerMinute { get; set; } = 15;
    public int PasswordConcurrency { get; set; } = 8;
    public int SsoProtocolConcurrency { get; set; } = 16;
    public int SsoPerProviderConcurrency { get; set; } = 4;
    public int SensitiveIpPerMinute { get; set; } = 1200;
    public int RuntimeCommandPerUserPerMinute { get; set; } = 30;
    public int PatchConcurrency { get; set; } = 2;
    public int PatchPerUserConcurrency { get; set; } = 1;
    public int SubmissionPerUserPerMinute { get; set; } = 30;
    public int SubmissionConcurrency { get; set; } = 16;
    public int SubmissionPerUserConcurrency { get; set; } = 2;
}
