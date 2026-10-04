using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Runner.Composition;
using NoCTF.Persistence.Sqlite;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CompetitionEventRegistrationTests
{
    [Test]
    public async Task Competition_event_contracts_use_codegen_visible_registrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:Generating"] = "true",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddNoCtfSqliteDevelopmentDatabase("event-registration-" + Guid.NewGuid().ToString("N"));
        services.AddNoCtfInfrastructure(configuration);

        foreach (var serviceType in new[]
                 {
                     typeof(ICompetitionEventStore),
                     typeof(ICompetitionEventRecorder)
                 })
        {
            var registration = services.Single(candidate =>
                candidate.ServiceType == serviceType);
            await Assert.That(registration.ImplementationType)
                .IsEqualTo(typeof(CompetitionEventStore));
            await Assert.That(registration.ImplementationFactory).IsNull();
        }
    }

    [Test]
    public async Task Standalone_runner_registers_the_persistent_event_recorder()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["RunnerScoring:CallbackBaseUrl"] = "https://api.internal"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddNoCtfRunner(configuration);

        var registration = services.Single(candidate =>
            candidate.ServiceType == typeof(ICompetitionEventRecorder));
        await Assert.That(registration.ImplementationType)
            .IsEqualTo(typeof(CompetitionEventStore));
        await Assert.That(registration.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }
}
