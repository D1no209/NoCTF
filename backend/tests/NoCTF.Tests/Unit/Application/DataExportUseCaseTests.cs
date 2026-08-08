using NSubstitute;
using NoCTF.Application.DataExports;
using NoCTF.Domain.DataExports;

namespace NoCTF.Tests.Unit.Application;

public sealed class DataExportUseCaseTests
{
    [Test]
    public async Task Protected_flag_exports_require_a_human_administrator_and_a_reason()
    {
        var store = Substitute.For<IDataExportStore>();
        var useCase = new RequestDataExport(store);
        var competitionId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        var nonAdministrator = await useCase.ExecuteAsync(new(
            DataExportScope.CompetitionArchive,
            competitionId,
            requesterId,
            false,
            true,
            true,
            "Appeal review"));
        var botAdministrator = await useCase.ExecuteAsync(new(
            DataExportScope.CompetitionArchive,
            competitionId,
            requesterId,
            true,
            false,
            true,
            "Appeal review"));
        var missingReason = await useCase.ExecuteAsync(new(
            DataExportScope.CompetitionArchive,
            competitionId,
            requesterId,
            true,
            true,
            true,
            "short"));

        await Assert.That(nonAdministrator.Failure)
            .IsEqualTo(RequestDataExportFailure.ProtectedFlagsRequireAdministrator);
        await Assert.That(botAdministrator.Failure)
            .IsEqualTo(RequestDataExportFailure.ProtectedFlagsRequireHuman);
        await Assert.That(missingReason.Failure)
            .IsEqualTo(RequestDataExportFailure.ReasonRequired);
        await store.DidNotReceiveWithAnyArgs().RequestAsync(default!, default);
    }

    [Test]
    public async Task Valid_export_requests_are_normalized_before_persistence()
    {
        var store = Substitute.For<IDataExportStore>();
        var export = new DataExportView(
            Guid.NewGuid(),
            DataExportScope.CompetitionArchive,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            true,
            "Appeal review",
            DataExportStatus.Queued,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        store.RequestAsync(
                Arg.Any<RequestDataExportCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new RequestDataExportResult(export));
        var useCase = new RequestDataExport(store);

        var result = await useCase.ExecuteAsync(new(
            DataExportScope.CompetitionArchive,
            export.CompetitionId,
            export.RequestedByUserId,
            true,
            true,
            true,
            "  Appeal review  "));

        await Assert.That(result.Export).IsEqualTo(export);
        await store.Received(1).RequestAsync(
            Arg.Is<RequestDataExportCommand>(command =>
                command != null && command.Reason == "Appeal review"),
            Arg.Any<CancellationToken>());
    }
}
