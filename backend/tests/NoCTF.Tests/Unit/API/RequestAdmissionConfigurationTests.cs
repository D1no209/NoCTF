using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Messaging;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class RequestAdmissionConfigurationTests
{
    [Test]
    public async Task Runtime_and_submission_limits_use_the_bound_options()
    {
        var options = Options.Create(new RequestAdmissionOptions
        {
            SensitiveIpPerMinute = 7,
            RuntimeCommandPerUserPerMinute = 3,
            SubmissionPerUserPerMinute = 2
        });

        var runtimeAdmission = new RecordingAdmission();
        await InvokeAsync(
            runtimeAdmission,
            options,
            new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand));
        await Assert.That(runtimeAdmission.Rates.Select(rate => rate.Limit))
            .IsEquivalentTo([7, 3]);

        var submissionAdmission = new RecordingAdmission();
        await InvokeAsync(
            submissionAdmission,
            options,
            new EnableRateLimitingAttribute("submission"));
        await Assert.That(submissionAdmission.Rates.Select(rate => rate.Limit))
            .IsEquivalentTo([7, 2]);
    }

    private static async Task InvokeAsync(
        RecordingAdmission admission,
        IOptions<RequestAdmissionOptions> options,
        object endpointMetadata)
    {
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "test"))
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(endpointMetadata),
            "test"));
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        var source = Substitute.For<IRequestSourceAddress>();
        source.Address.Returns("192.0.2.10");

        await new RequestAdmissionMiddleware(_ => Task.CompletedTask).InvokeAsync(
            context,
            admission,
            source,
            options,
            Substitute.For<IPatchUploadStore>(),
            new PostCommitDispatchStatus());
    }

    private sealed class RecordingAdmission : IRequestAdmission
    {
        public IReadOnlyList<RateQuota> Rates { get; private set; } = [];

        public ValueTask<IRequestAdmissionLease> AcquireAsync(
            IReadOnlyList<RateQuota> rates,
            IReadOnlyList<ConcurrentQuota> concurrency,
            CancellationToken ct)
        {
            Rates = rates;
            return ValueTask.FromResult<IRequestAdmissionLease>(new Lease(ct));
        }
    }

    private sealed class Lease(CancellationToken token) : IRequestAdmissionLease
    {
        public CancellationToken Token { get; } = token;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
