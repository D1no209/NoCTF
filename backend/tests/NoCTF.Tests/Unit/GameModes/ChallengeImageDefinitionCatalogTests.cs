using System.Text.Json;
using NoCTF.Application.Challenges.Images;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ChallengeImageDefinitionCatalogTests
{
    private const string DigestA = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Compose_and_checker_images_are_discovered_and_replaced_without_losing_services()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                """
                services:
                  web:
                    image: registry.example/web:v1
                  worker:
                    image: registry.example/worker:v2
                """,
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(1, 1, 1),
                    ["worker"] = new(1, 1, 1)
                }));
        var json = JsonSerializer.Serialize(new AwdChallengeConfiguration(
            AwdChallengeConfiguration.CurrentSchemaVersion,
            Runtime: runtime,
            Checker: new(new RunnerJobConfiguration("registry.example/checker:latest"))), JsonOptions);
        var catalog = new ChallengeImageDefinitionCatalog();

        var read = catalog.Read(GameMode.Awd, json);

        await Assert.That(read.Succeeded).IsTrue();
        await Assert.That(read.Images!).Count().IsEqualTo(3);
        var replacements = new Dictionary<ChallengeImageLocation, string>
        {
            [new(ChallengeImageLocationKind.RuntimeComposeService, "web")] = $"registry.example/web@{DigestA}",
            [new(ChallengeImageLocationKind.RuntimeComposeService, "worker")] = $"registry.example/worker@{DigestB}",
            [new(ChallengeImageLocationKind.Checker)] = $"registry.example/checker@{DigestC}"
        };

        var pinned = catalog.Replace(GameMode.Awd, json, replacements);
        var pinnedRead = catalog.Read(GameMode.Awd, pinned);

        await Assert.That(pinnedRead.Succeeded).IsTrue();
        await Assert.That(pinnedRead.Images!.Select(image => image.Image))
            .IsEquivalentTo(replacements.Values);
    }

    [Test]
    public async Task Replacing_an_already_pinned_container_with_the_same_digest_preserves_exact_json()
    {
        var image = $"registry.example/challenge@{DigestA}";
        var json = JsonSerializer.Serialize(new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            Points: null,
            BloodRewards: null,
            Runtime: new(
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition(image))), JsonOptions);
        var catalog = new ChallengeImageDefinitionCatalog();

        var result = catalog.Replace(
            GameMode.Ctf,
            json,
            new Dictionary<ChallengeImageLocation, string>
            {
                [new(ChallengeImageLocationKind.RuntimeContainer)] = image
            });

        await Assert.That(result).IsEqualTo(json);
    }

    [Test]
    [Arguments("nginx:latest", "registry-1.docker.io", "library/nginx", "latest", false)]
    [Arguments("ghcr.io/acme/challenge:v1", "ghcr.io", "acme/challenge", "v1", false)]
    [Arguments("localhost:5000/acme/challenge", "localhost:5000", "acme/challenge", "latest", false)]
    [Arguments("registry.example/acme/repo__name:v1", "registry.example", "acme/repo__name", "v1", false)]
    [Arguments("registry.example/acme/repo---name:v1", "registry.example", "acme/repo---name", "v1", false)]
    [Arguments("nginx@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "registry-1.docker.io", "library/nginx", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    public async Task Container_image_reference_parsing_is_canonical(
        string input,
        string registry,
        string repository,
        string reference,
        bool digest)
    {
        var parsed = ContainerImageReference.TryParse(input, out var image);

        await Assert.That(parsed).IsTrue();
        await Assert.That(image.RegistryHost).IsEqualTo(registry);
        await Assert.That(image.Repository).IsEqualTo(repository);
        await Assert.That(image.Reference).IsEqualTo(reference);
        await Assert.That(image.IsDigest).IsEqualTo(digest);
    }

    [Test]
    [Arguments("https://registry.example/repo:v1")]
    [Arguments("registry.example/Upper:v1")]
    [Arguments("registry.example/repo@sha256:abc")]
    [Arguments("user:password@registry.example/repo:v1")]
    [Arguments("registry.example?redirect/repo:v1")]
    [Arguments("registry.example#fragment/repo:v1")]
    public async Task Invalid_container_image_references_are_rejected(string input) =>
        await Assert.That(ContainerImageReference.TryParse(input, out _)).IsFalse();
}
