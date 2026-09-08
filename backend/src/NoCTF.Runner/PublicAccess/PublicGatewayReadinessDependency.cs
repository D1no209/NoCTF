using NoCTF.Hosting.Health;

namespace NoCTF.Runner.PublicAccess;

public sealed class PublicGatewayReadinessDependency(IServiceProvider services) : IReadinessDependency
{
    public string Name => "public-gateway";
    public bool FailureIsCritical => true;

    public Task CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var agent = services.GetService<PublicGatewayAgent>();
        return agent is null
            ? Task.FromException(new InvalidOperationException("The configured public gateway agent is unavailable."))
            : agent.CheckReadinessAsync(cancellationToken);
    }
}
