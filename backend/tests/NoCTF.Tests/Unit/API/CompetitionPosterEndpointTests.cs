using System.Net;
using FastEndpoints;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Storage;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionPosterEndpointTests
{
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid PosterFileId = Guid.CreateVersion7();

    [Test]
    public async Task Matching_revision_is_immutable_while_unversioned_downloads_are_not_cached()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();

        using var versioned = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/poster?revision={PosterFileId:N}");
        using var unversioned = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/poster");

        await Assert.That(versioned.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(versioned.Headers.CacheControl?.Public).IsTrue();
        await Assert.That(versioned.Headers.CacheControl?.MaxAge)
            .IsEqualTo(TimeSpan.FromDays(365));
        await Assert.That(versioned.Headers.CacheControl?.Extensions
            .Any(extension => extension.Name == "immutable")).IsTrue();
        await Assert.That(unversioned.Headers.CacheControl?.NoStore).IsTrue();
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        var references = Substitute.For<IBusinessFileReferenceStore>();
        references.GetCompetitionPosterAsync(
                CompetitionId,
                Arg.Any<CancellationToken>())
            .Returns(new BusinessFileReference(
                PosterFileId,
                "competitions/poster.webp",
                "poster.webp",
                "image/webp"));
        var objects = Substitute.For<IStore>();
        objects.OpenRead(
                "competitions/poster.webp",
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var images = new ManageBusinessImages(
            references,
            objects,
            new ManagedFileUploads(
                Substitute.For<IManagedFileUploadRegistry>(),
                objects));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetCompetitionPosterEndpoint).Assembly];
            options.Filter = type => type == typeof(GetCompetitionPosterEndpoint);
        });
        builder.Services.AddSingleton(images);
        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }
}
