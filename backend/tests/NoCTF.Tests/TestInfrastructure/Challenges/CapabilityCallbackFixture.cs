using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using k8s.Models;
using k8s;
using NoCTF.Domain.Runtime;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Internal;
using NoCTF.Application.Authentication;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests;

internal sealed class CapabilityCallbackFixture : IAsyncDisposable
{
    internal const string PythonImage = "docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8";
    private WebApplication? server;
    private IContainer? gateway;
    public IOptions<RunnerScoringOptions> Scoring { get; private set; } = null!;
    public Uri PublicBaseUrl { get; private set; } = null!;
    private k8s.IKubernetes? kubernetes;
    private string? gatewayName;

    public static async Task<CapabilityCallbackFixture> CreateAsync(RuntimeExecutionFixture runtime, CancellationToken ct)
    {
        var fixture = new CapabilityCallbackFixture();
        try
        {
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = key }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://0.0.0.0:0");
            builder.Services.AddFastEndpoints(options =>
            {
                options.DisableAutoDiscovery = true; options.Assemblies = [typeof(RecordCheckResultEndpoint).Assembly];
                options.Filter = type => type == typeof(RecordCheckResultEndpoint) || type == typeof(RecordCheckResultValidator)
                    || type == typeof(RecordControlResultEndpoint) || type == typeof(RecordControlResultValidator);
            });
            builder.Services.SwaggerDocument(); builder.Services.AddNoCtfAuthentication(configuration);
            builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(runtime.Db.Database.GetConnectionString()));
            builder.Services.AddDbContextFactory<NoCtfDbContext>(options => options.UseNpgsql(runtime.Db.Database.GetConnectionString()), ServiceLifetime.Scoped);
            builder.Services.AddScoped<TemplateCapabilityTestExecutionStore>();
            builder.Services.AddScoped<NoCTF.Application.Challenges.Capabilities.IControlResultStore, NoCTF.Infrastructure.Challenges.Capabilities.ControlExecutionStore>();
            builder.Services.AddScoped<ITemplateCapabilityCheckResultStore>(services => services.GetRequiredService<TemplateCapabilityTestExecutionStore>());
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(runtime.Application.MessageBus);
            builder.Services.AddScoped<IPostCommitMessagePublisher, WolverinePostCommitMessagePublisher>();
            builder.Services.AddScoped<IInternalResultStore, InternalResultStore>(); builder.Services.AddScoped<RecordInternalResult>();
            builder.Services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = key }));
            builder.Services.AddSingleton<PlatformSecretProtector>();
            builder.Services.AddSingleton<NoCTF.Application.Authentication.Mfa.IMfaCryptography, NoCTF.Infrastructure.Authentication.Mfa.MfaCryptography>();
            builder.Services.AddScoped<NoCTF.Application.Authentication.Mfa.IMfaAuthenticationStore, NoCTF.Infrastructure.Authentication.Mfa.MfaAuthenticationStore>();
            fixture.server = builder.Build();
            fixture.server.UseAuthentication(); fixture.server.UseAuthorization(); fixture.server.UseNoCtfEndpoints();
            await fixture.server.StartAsync(ct);
            var port = new Uri(fixture.server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
            fixture.PublicBaseUrl = new($"http://127.0.0.1:{port}");
            fixture.Scoring = Options.Create(new RunnerScoringOptions { SigningKey = key, CallbackBaseUrl = new Uri($"http://{Environment.MachineName}:{port}") });
            if (runtime.Provider == RuntimeProvider.Kubernetes)
            {
                fixture.kubernetes = runtime.Kubernetes;
                fixture.gatewayName = runtime.CallbackContainerName;
                var options = runtime.KubernetesOptions;
                var hostAddress = Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CALLBACK_HOST_IP")
                    ?? throw new InvalidOperationException("The isolated cluster callback host address is required.");
                var labels = new Dictionary<string, string> { ["app"] = fixture.gatewayName,
                    [options.CallbackPodLabelKey] = options.CallbackPodLabelValue };
                await fixture.kubernetes.CoreV1.CreateNamespacedPodAsync(new()
                {
                    Metadata = new() { Name = fixture.gatewayName, Labels = labels },
                    Spec = new()
                    {
                        AutomountServiceAccountToken = false,
                        NodeSelector = new Dictionary<string, string> { [NoCTF.Runtime.Kubernetes.Configuration.KubernetesRuntimeOptions.PodPidsLimitNodeLabel] = "128" },
                        HostAliases = [new() { Ip = hostAddress, Hostnames = ["host.docker.internal"] }],
                        SecurityContext = new() { RunAsNonRoot = true, RunAsUser = 10001, RunAsGroup = 10001 },
                        Containers = [new()
                        {
                            Name = "callback", Image = PythonImage,
                            Command = ["python", "-c", await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "callback_proxy.py"), ct)],
                            Env = [new() { Name = "API_PORT", Value = port.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
                            ReadinessProbe = new() { TcpSocket = new() { Port = port }, PeriodSeconds = 1 },
                            Resources = new() { Requests = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("50m"), ["memory"] = new("64Mi") },
                                Limits = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("250m"), ["memory"] = new("128Mi") } },
                            SecurityContext = new() { AllowPrivilegeEscalation = false, ReadOnlyRootFilesystem = true, Capabilities = new() { Drop = ["ALL"] } }
                        }]
                    }
                }, "noctf", cancellationToken: ct);
                await fixture.kubernetes.CoreV1.CreateNamespacedServiceAsync(new()
                {
                    Metadata = new() { Name = fixture.gatewayName },
                    Spec = new() { Selector = new Dictionary<string, string> { ["app"] = fixture.gatewayName },
                        Ports = [new() { Port = port, TargetPort = port }] }
                }, "noctf", cancellationToken: ct);
                using var ready = CancellationTokenSource.CreateLinkedTokenSource(ct);
                ready.CancelAfter(TimeSpan.FromMinutes(2));
                while (true)
                {
                    var pod = await fixture.kubernetes.CoreV1.ReadNamespacedPodAsync(fixture.gatewayName, "noctf", cancellationToken: ready.Token);
                    if (pod.Status?.Conditions?.Any(condition => condition.Type == "Ready" && condition.Status == "True") == true) break;
                    await Task.Delay(500, ready.Token);
                }
                fixture.Scoring = Options.Create(new RunnerScoringOptions { SigningKey = key,
                    CallbackBaseUrl = new($"http://{fixture.gatewayName}.noctf.svc.cluster.local:{port}") });
                return fixture;
            }
            fixture.gateway = new ContainerBuilder(PythonImage).WithName(runtime.CallbackContainerName).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
                .WithExtraHost("host.docker.internal", "host-gateway").WithEnvironment("API_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "callback_proxy.py"), "/work/")
                .WithCommand("python", "/work/callback_proxy.py").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(port)).Build();
            await fixture.gateway.StartAsync(ct);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (kubernetes is not null && gatewayName is not null)
        {
            await kubernetes.CoreV1.DeleteNamespacedServiceAsync(gatewayName, "noctf");
            await kubernetes.CoreV1.DeleteNamespacedPodAsync(gatewayName, "noctf", body: new V1DeleteOptions { GracePeriodSeconds = 0 });
        }
        if (gateway is not null) await gateway.DisposeAsync();
        if (server is not null) await server.DisposeAsync();
    }
}
