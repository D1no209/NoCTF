using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Access;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionListAccessTests
{
    [Test]
    public async Task Hidden_items_are_listed_only_for_administrators_and_collaborators()
    {
        var publicCompetition = Competition(CompetitionAccessMode.Public);
        var hiddenCompetition = Competition(CompetitionAccessMode.StaffOnly);
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();

        var anonymous = Substitute.For<IUserContext>();
        var anonymousItems = await CompetitionAudiencePolicy.FilterCatalogAsync(
            [publicCompetition, hiddenCompetition],
            anonymous.UserId,
            anonymous.IsAdministrator,
            authorizer,
            CancellationToken.None);

        var collaborator = Substitute.For<IUserContext>();
        collaborator.UserId.Returns(Guid.CreateVersion7());
        authorizer.CanObserveAsync(
                collaborator.UserId,
                hiddenCompetition.Id,
                CancellationToken.None)
            .Returns(true);
        var collaboratorItems = await CompetitionAudiencePolicy.FilterCatalogAsync(
            [publicCompetition, hiddenCompetition],
            collaborator.UserId,
            collaborator.IsAdministrator,
            authorizer,
            CancellationToken.None);

        var administrator = Substitute.For<IUserContext>();
        administrator.UserId.Returns(Guid.CreateVersion7());
        administrator.IsAdministrator.Returns(true);
        var administratorItems = await CompetitionAudiencePolicy.FilterCatalogAsync(
            [publicCompetition, hiddenCompetition],
            administrator.UserId,
            administrator.IsAdministrator,
            authorizer,
            CancellationToken.None);

        await Assert.That(anonymousItems.Select(item => item.Id))
            .IsEquivalentTo([publicCompetition.Id]);
        await Assert.That(collaboratorItems.Select(item => item.Id))
            .IsEquivalentTo([publicCompetition.Id, hiddenCompetition.Id]);
        await Assert.That(administratorItems.Select(item => item.Id))
            .IsEquivalentTo([publicCompetition.Id, hiddenCompetition.Id]);
    }

    private static CompetitionView Competition(CompetitionAccessMode accessMode)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            Guid.CreateVersion7(),
            accessMode.ToString(),
            null,
            GameMode.Ctf,
            now,
            now.AddHours(1),
            CompetitionStatus.Published,
            true,
            5,
            1,
            Guid.CreateVersion7(),
            AccessMode: accessMode);
    }
}
