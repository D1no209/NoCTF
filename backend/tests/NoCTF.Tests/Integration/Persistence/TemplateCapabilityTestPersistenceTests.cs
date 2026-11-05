using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TemplateCapabilityTestPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Fix_only_tests_use_independent_targets_and_replay_the_same_acceptance_without_gameplay_facts(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var template = await fixture.Db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.TemplateId, ct);
        template.Definition = new()
        {
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Image = RuntimeExecutionFixture.Image }] },
            Capabilities = [new FixCapabilityDefinition { TargetServiceName = "web", Entrypoint = "fix.sh" }]
        };
        await fixture.Db.SaveChangesAsync(ct);
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool }));
        var interactiveStore = new ChallengeTestRuntimeStore(fixture.Db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Publisher);
        var interactive = await interactiveStore.MutateAsync(new(fixture.TemplateId, fixture.OwnerId, false,
            RuntimeAction.Start, null, fixture.Now), ct);
        var store = new TemplateCapabilityTestStore(fixture.Db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Publisher, TimeProvider.System);
        var directory = Path.GetFullPath(Path.Combine("artifacts", "template-capability-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        using var objects = StorageFactory.Disk(directory);
        var uploads = new ManagedFileUploads(new ManagedFileUploadRegistry(fixture.Db, fixture.Publisher,
            NullLogger<ManagedFileUploadRegistry>.Instance), objects);
        var tests = new StartTemplateCapabilityTest(store, uploads, TimeProvider.System);
        var requestId = Guid.NewGuid();
        using var archive = Archive();
        var access = new ChallengeTestAccess(fixture.TemplateId, fixture.OwnerId, false);
        var first = await tests.FixAsync(access, requestId, false, "fix.tar.gz", "application/gzip", archive, ct);
        await Assert.That(first.Succeeded).IsTrue();
        archive.Position = 0;
        var replay = await tests.FixAsync(access, requestId, false, "fix.tar.gz", "application/gzip", archive, ct);
        await Assert.That(replay.Value).IsEqualTo(first.Value);
        archive.Position = 0;
        var second = await tests.FixAsync(access, Guid.NewGuid(), false, "fix.tar.gz", "application/gzip", archive, ct);
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(second.Value!.RuntimeInstanceId == first.Value!.RuntimeInstanceId).IsFalse();
        await Assert.That((await interactiveStore.FindAsync(fixture.TemplateId, fixture.OwnerId, false, ct))!.Id).IsEqualTo(interactive.Runtime!.Id);
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        var runtimes = await fixture.Db.RuntimeInstances.AsNoTracking().AsSplitQuery().ToArrayAsync(ct);
        await Assert.That(runtimes.Count(value => value.TemplateTestUse == TemplateTestUse.CapabilityVerification)).IsEqualTo(2);
        await Assert.That(runtimes.All(value => value.CompetitionId is null && value.CompetitionChallengeId is null && value.TeamId is null && value.GameplayFactId is null)).IsTrue();
        var view = await store.FindAsync(access, first.Value.ExecutionId, ct);
        await Assert.That(view!.Kind).IsEqualTo(TemplateCapabilityTestKind.Fix);
        await Assert.That(view.State).IsEqualTo(TemplateCapabilityTestState.Queued);
        await Assert.That(await store.FindAsync(access with { ActorUserId = Guid.NewGuid() }, first.Value.ExecutionId, ct)).IsNull();
        archive.Position = 0;
        var conflict = await tests.FixAsync(access, requestId, true, "fix.tar.gz", "application/gzip", archive, ct);
        await Assert.That(conflict.FailureCode).IsEqualTo(TemplateCapabilityTestFailure.CapabilityMissing);
    }

    [Test, Timeout(300_000)]
    public async Task Questionnaire_preview_reads_saved_content_without_issuing_a_session_or_creating_an_attempt(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool }));
        var store = new TemplateCapabilityTestStore(fixture.Db, new ChallengeRuntimeTemplateCatalog(), placement,
            fixture.Application.Publisher, TimeProvider.System);
        await using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var tests = new TestChallengeCapabilities(store, new(new HttpControlObservationClient(services.GetRequiredService<IHttpClientFactory>()),
            TimeProvider.System), TimeProvider.System);
        var material = await fixture.Db.Challenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == fixture.ChallengeId, ct);
        var revision = material.Definition!.Questionnaire!.CurrentRevision;
        var answers = revision.Questions.Select(question => new NoCTF.Application.Challenges.Capabilities.QuestionnaireAnswer(question.Id,
            question.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToArray())).ToArray();
        var result = await tests.QuestionnaireAsync(new(fixture.ChallengeId, fixture.OwnerId, false), answers, ct);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Questions.All(question => question.Result == QuestionnaireAnswerResult.Correct)).IsTrue();
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await fixture.Db.QuestionnaireSessions.CountAsync(ct)).IsEqualTo(0);
        var denied = await tests.QuestionnaireAsync(new(fixture.ChallengeId, fixture.FirstUserId, false), answers, ct);
        await Assert.That(denied.FailureCode).IsEqualTo(TemplateCapabilityTestFailure.NotFound);
    }

    [Test, Timeout(300_000)]
    public async Task Admission_rechecks_the_saved_archive_limit_after_upload(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var template = await fixture.Db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.TemplateId, ct);
        template.Definition = new()
        {
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Image = RuntimeExecutionFixture.Image }] },
            Capabilities = [new FixCapabilityDefinition { TargetServiceName = "web", Entrypoint = "fix.sh", MaximumUploadBytes = 4096 }]
        };
        await fixture.Db.SaveChangesAsync(ct);
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool }));
        var store = new TemplateCapabilityTestStore(fixture.Db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Publisher, TimeProvider.System);
        var access = new ChallengeTestAccess(template.Id, fixture.OwnerId, false);
        await Assert.That((await store.ReadAsync(access, ChallengeCapabilityKind.Fix, ct)).Succeeded).IsTrue();
        var directory = Path.GetFullPath(Path.Combine("artifacts", "template-limit-admission", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        using var objects = StorageFactory.Disk(directory);
        var uploads = new ManagedFileUploads(new ManagedFileUploadRegistry(fixture.Db, fixture.Publisher,
            NullLogger<ManagedFileUploadRegistry>.Instance), objects);
        using var archive = Archive();
        var fileId = Guid.NewGuid();
        await uploads.CreateAsync(fileId, $"template-fix-tests/{fileId:N}", "fix.tar.gz", "application/gzip", archive, fixture.Now, ct);
        template.Definition.Fix!.MaximumUploadBytes = 1;
        await fixture.Db.SaveChangesAsync(ct);
        var admitted = await store.StartAsync(new(access, Guid.NewGuid(), TemplateCapabilityTestKind.Fix, fileId,
            "saved-archive", DateTimeOffset.UtcNow), ct);
        await Assert.That(admitted.FailureCode).IsEqualTo(TemplateCapabilityTestFailure.ArchiveTooLarge);
        await Assert.That(await fixture.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        await uploads.AbandonAsync(fileId);
    }

    private static MemoryStream Archive()
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, true))
        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("printf repaired > /service-state\n")))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = content });
        output.Position = 0;
        return output;
    }
}
