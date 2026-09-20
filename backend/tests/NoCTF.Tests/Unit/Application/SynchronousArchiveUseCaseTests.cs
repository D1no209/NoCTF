using NSubstitute;
using NoCTF.Application.Exports;

namespace NoCTF.Tests.Unit.Application;

public sealed class SynchronousArchiveUseCaseTests
{
    [Test]
    public async Task Protected_competition_exports_require_an_administrator_and_reason()
    {
        var generator = Substitute.For<ISynchronousArchiveGenerator>();
        var useCase = new ExportCompetitionArchive(generator);
        var competitionId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        var nonAdministrator = await useCase.ExecuteAsync(new(
            competitionId, requesterId, false, true, "Appeal review"));
        var missingReason = await useCase.ExecuteAsync(new(
            competitionId, requesterId, true, true, "short"));

        await Assert.That(nonAdministrator.Failure)
            .IsEqualTo(SynchronousArchiveFailure.ProtectedFlagsRequireAdministrator);
        await Assert.That(missingReason.Failure)
            .IsEqualTo(SynchronousArchiveFailure.ReasonRequired);
        await generator.DidNotReceiveWithAnyArgs()
            .GenerateCompetitionAsync(default!, default);
    }

    [Test]
    public async Task Valid_competition_export_trims_reason_and_delegates_once()
    {
        var generator = Substitute.For<ISynchronousArchiveGenerator>();
        generator.GenerateCompetitionAsync(
                Arg.Any<ExportCompetitionArchiveCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new SynchronousArchiveResult(
                new SynchronousArchive(Stream.Null, "archive.zip", "application/zip")));
        var useCase = new ExportCompetitionArchive(generator);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), true, true, "  Appeal review  "));

        await Assert.That(result.Archive).IsNotNull();
        await generator.Received(1).GenerateCompetitionAsync(
            Arg.Is<ExportCompetitionArchiveCommand>(command =>
                command != null && command.Reason == "Appeal review"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Platform_audit_export_requires_administrator_and_complete_range()
    {
        var generator = Substitute.For<ISynchronousArchiveGenerator>();
        var useCase = new ExportPlatformAuditArchive(generator);
        var now = DateTimeOffset.UtcNow;

        var nonAdministrator = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), false, null, null, null, null, null));
        var incompleteRange = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), true, null, null, null, now, null));
        var reversedRange = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), true, null, null, null, now, now.AddMinutes(-1)));

        await Assert.That(nonAdministrator.Failure)
            .IsEqualTo(SynchronousArchiveFailure.Forbidden);
        await Assert.That(incompleteRange.Failure)
            .IsEqualTo(SynchronousArchiveFailure.InvalidQuery);
        await Assert.That(reversedRange.Failure)
            .IsEqualTo(SynchronousArchiveFailure.InvalidQuery);
        await generator.DidNotReceiveWithAnyArgs()
            .GeneratePlatformAuditAsync(default!, default);
    }
}
