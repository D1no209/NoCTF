using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Runtime.Targets;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class RuntimeParticipantUrlProjectionTests
{
    [Test]
    public async Task Participant_urls_are_filtered_from_the_latest_definition()
    {
        var catalog = new ChallengeRuntimeTemplateCatalog();
        var endpoints = new[]
        {
            new RuntimeAccessEndpointView(0, "tcp://host:30001", null, null),
            new RuntimeAccessEndpointView(1, "tcp://host:30002", null, null)
        };
        var original = Definition(
            new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31337),
            new("tcp://{HOST}:{PORT}", RuntimeExposure.Participants, 31338));
        var updated = Definition(
            new("tcp://{HOST}:{PORT}", RuntimeExposure.Participants, 31337),
            new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31338));

        var before = RuntimeParticipantUrlProjection.Filter(
            catalog,
            original,
            endpoints);
        var after = RuntimeParticipantUrlProjection.Filter(
            catalog,
            updated,
            endpoints);

        await Assert.That(before.Select(endpoint => endpoint.DirectAddress).OfType<string>())
            .IsEquivalentTo(["tcp://host:30002"]);
        await Assert.That(after.Select(endpoint => endpoint.DirectAddress).OfType<string>())
            .IsEquivalentTo(["tcp://host:30001"]);
    }

    private static ChallengeDefinition Definition(params RuntimeUrlBinding[] bindings) =>
        new CtfChallengeDefinition
        {
            Runtime = new ContainerChallengeRuntimeTemplate
            {
                Allocation = PersistedRuntimeAllocation.PerTeam,
                Image = "example.invalid/runtime:test",
                Capabilities = [new() { Add = false, Name = "ALL" }],
                UrlBindings = bindings.Select((binding, position) => new ChallengeRuntimeUrlBinding
                {
                    Position = position,
                    UrlTemplate = binding.UrlTemplate,
                    Exposure = (PersistedRuntimeExposure)binding.Exposure,
                    ContainerPort = binding.ContainerPort,
                    ServiceName = binding.ServiceName,
                    VmId = binding.VmId,
                    GuestPort = binding.GuestPort
                }).ToList()
            }
        };
}
