using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Capabilities;

namespace NoCTF.Tests.Integration.Gameplay;

[Category("Integration")]
public sealed class ControlCapabilityTests
{
    [Test]
    [Arguments("controlled", ControlResult.Controlled, null)]
    [Arguments("empty", ControlResult.Uncontrolled, null)]
    [Arguments("invalid", ControlResult.Indeterminate, CapabilityExecutionFailure.InvalidEvidence)]
    [Arguments("oversized", ControlResult.Indeterminate, CapabilityExecutionFailure.InvalidEvidence)]
    [Arguments("redirect", ControlResult.Indeterminate, CapabilityExecutionFailure.RuntimeUnavailable)]
    [Arguments("unavailable", ControlResult.Indeterminate, CapabilityExecutionFailure.RuntimeUnavailable)]
    public async Task Http_observations_preserve_objective_control_and_failure_evidence(string scenario, ControlResult expected,
        CapabilityExecutionFailure? failure, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpClient(HttpControlObservationClient.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        await using var app = builder.Build();
        app.Run(async context =>
        {
            if (scenario == "redirect") { context.Response.StatusCode = 302; context.Response.Headers.Location = "/controlled"; return; }
            if (scenario == "unavailable") { context.Response.StatusCode = 503; return; }
            byte[] bytes = scenario switch
            {
                "controlled" => Encoding.UTF8.GetBytes("controller-7"),
                "empty" => [],
                "invalid" => [0xC0, 0xAF],
                "oversized" => Encoding.UTF8.GetBytes(new string('x', 4097)),
                _ => throw new InvalidOperationException("Unexpected test scenario.")
            };
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        await app.StartAsync(ct);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpControlObservationClient(app.Services.GetRequiredService<IHttpClientFactory>());
        var factId = Guid.NewGuid(); var runtimeId = Guid.NewGuid();
        var result = await new ObserveControl(client, TimeProvider.System).ExecuteAsync(new(factId, runtimeId,
            new ControlCapabilityDefinition(), new Uri(address), DateTimeOffset.UtcNow.AddSeconds(10)), ct);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Result).IsEqualTo(expected);
        await Assert.That(result.Value.ExecutionFailure).IsEqualTo(failure);
        await Assert.That(result.Value.ExecutionId).IsEqualTo(factId);
        await Assert.That(result.Value.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(result.Value.ControllerIdentifier).IsEqualTo(expected == ControlResult.Controlled ? "controller-7" : null);
        await Assert.That(result.Value.ToString().Contains("controller-7", StringComparison.Ordinal)).IsFalse();
        await app.StopAsync(ct);
    }
}
