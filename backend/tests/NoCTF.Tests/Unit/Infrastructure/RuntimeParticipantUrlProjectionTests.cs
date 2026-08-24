using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
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
        var urls = new[] { "tcp://host:30001", "tcp://host:30002" };
        var original = Definition(
            new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31337),
            new("tcp://{HOST}:{PORT}", RuntimeExposure.Participants, 31338));
        var updated = Definition(
            new("tcp://{HOST}:{PORT}", RuntimeExposure.Participants, 31337),
            new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31338));

        var before = RuntimeParticipantUrlProjection.Filter(
            catalog,
            GameMode.Ctf,
            original,
            urls);
        var after = RuntimeParticipantUrlProjection.Filter(
            catalog,
            GameMode.Ctf,
            updated,
            urls);

        await Assert.That(before).IsEquivalentTo([urls[1]]);
        await Assert.That(after).IsEquivalentTo([urls[0]]);
    }

    private static string Definition(params RuntimeUrlBinding[] bindings) =>
        JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                Runtime: new ChallengeRuntimeTemplate(
                    RuntimeAllocation.PerTeam,
                    new ContainerRuntimeDefinition("example.invalid/runtime:test"),
                    UrlBindings: bindings)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
