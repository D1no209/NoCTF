using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Registration;
using ArchUnitNET.Loader;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace NoCTF.Tests.Architecture;

public class DependencyRulesTests
{
    [Test]
    public async Task Domain_has_no_framework_or_adapter_references()
    {
        var references = typeof(User).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        await Assert.That(references).DoesNotContain("Microsoft.EntityFrameworkCore");
        await Assert.That(references).DoesNotContain("Marten");
        await Assert.That(references).DoesNotContain("Wolverine");
        await Assert.That(references).DoesNotContain("Rebus");
    }

    [Test]
    public async Task Application_has_no_framework_or_persistence_references()
    {
        var references = typeof(SubmitFlag).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        await Assert.That(references).DoesNotContain("Microsoft.EntityFrameworkCore");
        await Assert.That(references).DoesNotContain("Marten");
        await Assert.That(references).DoesNotContain("Wolverine");
        await Assert.That(references).DoesNotContain("Rebus");
    }

    [Test]
    public async Task GameModes_are_compile_time_catalog_entries()
    {
        await Assert.That(GameModeCatalog.All).Count().IsEqualTo(4);
        await Assert.That(typeof(GameModeCatalog).Assembly.GetReferencedAssemblies().Select(reference => reference.Name))
            .DoesNotContain("Marten");
    }

    [Test]
    public async Task Scoring_facts_do_not_persist_scores()
    {
        await Assert.That(typeof(ScoringEvent).GetProperty(nameof(ScoringEvent.Result))!.PropertyType)
            .IsEqualTo(typeof(ScoringResult));
        await Assert.That(typeof(ScoringEvent).GetProperty("Score")).IsNull();
        await Assert.That(typeof(ScoringEvent).GetProperty("ScoreDelta")).IsNull();
    }

    [Test]
    public async Task Legacy_stream_scoring_contracts_are_absent()
    {
        var application = typeof(SubmitFlag).Assembly;

        await Assert.That(application.GetType("NoCTF.Application.Submissions.Events.ISubmissionStreamEvent")).IsNull();
        await Assert.That(application.GetType("NoCTF.Application.Submissions.Events.SubmissionOutcome")).IsNull();
        await Assert.That(application.GetType("NoCTF.Application.Submissions.Events.SubmissionErrorCode")).IsNull();
        await Assert.That(application.GetType("NoCTF.Application.Scoring.Leaderboard.LeaderboardSnapshot")).IsNull();
    }

    [Test]
    public async Task ArchUnitNET_can_load_the_new_project_graph()
    {
        var architecture = new ArchLoader()
            .LoadAssemblies(
                typeof(User).Assembly,
                typeof(SubmitFlag).Assembly,
                typeof(GameModeCatalog).Assembly)
            .Build();

        await Assert.That(architecture).IsNotNull();
    }

    [Test]
    public async Task Application_types_do_not_depend_on_persistence_frameworks()
    {
        var architecture = new ArchLoader()
            .LoadAssemblies(
                typeof(User).Assembly,
                typeof(SubmitFlag).Assembly,
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly)
            .Build();
        var rule = Types()
            .That()
            .ResideInAssembly(typeof(SubmitFlag).Assembly)
            .Should()
            .NotDependOnAny(
                typeof(Microsoft.EntityFrameworkCore.DbContext));

        await Assert.That(rule.HasNoViolations(architecture)).IsTrue();
    }
}
