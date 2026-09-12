using NoCTF.API.Endpoints.Administration.Platform;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformInformationEndpointTests
{
    [Test]
    public async Task Information_lists_the_verified_platform_contributors()
    {
        var result = await new GetPlatformInformationEndpoint()
            .ExecuteAsync(CancellationToken.None);

        await Assert.That(result.Value!.Contributors)
            .Contains(new PlatformContributorResponse(
                "evnrowa",
                "https://avatars.githubusercontent.com/u/123802298?v=4"));
    }
}
