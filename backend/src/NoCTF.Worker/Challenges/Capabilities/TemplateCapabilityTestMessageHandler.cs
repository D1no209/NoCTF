using NoCTF.Application.Challenges.Testing;
using NoCTF.Infrastructure.Challenges.Testing;

namespace NoCTF.Worker.Challenges.Capabilities;

public sealed class TemplateCapabilityTestMessageHandler(TemplateCapabilityTestExecutionStore tests)
{
    public Task Handle(DispatchTemplateCapabilityTest message, CancellationToken ct) => tests.DispatchAsync(message, ct);
    public Task Handle(RecoverTemplateCapabilityTests message, CancellationToken ct) => tests.RecoverAsync(ct);
}
