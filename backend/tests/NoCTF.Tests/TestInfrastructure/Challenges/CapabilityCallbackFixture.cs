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
                options.Filter = type => type == typeof(RecordCheckResultEndpoint) || type == typeof(RecordCheckResultValidator);
            });
            builder.Services.SwaggerDocument(); builder.Services.AddNoCtfAuthentication(configuration);
            builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(runtime.Db.Database.GetConnectionString()));
            builder.Services.AddDbContextFactory<NoCtfDbContext>(options => options.UseNpgsql(runtime.Db.Database.GetConnectionString()), ServiceLifetime.Scoped);
            builder.Services.AddScoped<TemplateCapabilityTestExecutionStore>();
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
            fixture.Scoring = Options.Create(new RunnerScoringOptions { SigningKey = key, CallbackBaseUrl = new Uri($"http://{Environment.MachineName}:{port}") });
            fixture.gateway = new ContainerBuilder(PythonImage).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
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
        if (gateway is not null) await gateway.DisposeAsync();
        if (server is not null) await server.DisposeAsync();
    }
}
