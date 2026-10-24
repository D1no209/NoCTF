using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Tests.Unit.API;

public sealed class RetryCompetitionWebhookDeliveryEndpointTests
{
    [Test]
    public async Task Unauthorized_resource_access_cannot_change_delivery_state()
    {
        var store = Substitute.For<ICompetitionWebhookDeliveryStore>();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        var user = Substitute.For<IUserContext>();
        user.UserId.Returns(Guid.NewGuid());
        var endpoint = new RetryCompetitionWebhookDeliveryEndpoint(
            new(store, new FakeTimeProvider()), authorizer, user);

        var result = await endpoint.ExecuteAsync(new()
        {
            CompetitionId = Guid.NewGuid(), TargetId = Guid.NewGuid(), EventId = Guid.NewGuid()
        }, CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<ForbidHttpResult>();
        await store.DidNotReceive().RetryProjectionFailureAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
