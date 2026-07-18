using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
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
    }

    [Test]
    public async Task Application_has_no_framework_or_persistence_references()
    {
        var references = typeof(SubmitFlag).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        await Assert.That(references).DoesNotContain("Microsoft.EntityFrameworkCore");
        await Assert.That(references).DoesNotContain("Marten");
        await Assert.That(references).DoesNotContain("Wolverine");
    }

    [Test]
    public async Task GameModes_are_compile_time_catalog_entries()
    {
        await Assert.That(GameModeCatalog.All).Count().IsEqualTo(5);
        await Assert.That(typeof(GameModeCatalog).Assembly.GetReferencedAssemblies().Select(reference => reference.Name))
            .DoesNotContain("Marten");
    }

    [Test]
    public async Task Bounded_event_concepts_use_enums()
    {
        await Assert.That(typeof(ScoreAwarded).GetProperty(nameof(ScoreAwarded.Reason))!.PropertyType)
            .IsEqualTo(typeof(ScoringReason));
        await Assert.That(typeof(FlagSubmissionEvaluated).GetProperty(nameof(FlagSubmissionEvaluated.ErrorCode))!.PropertyType)
            .IsEqualTo(typeof(SubmissionErrorCode?));
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
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,
                typeof(Marten.IDocumentSession).Assembly,
                typeof(Wolverine.IMessageBus).Assembly)
            .Build();
        var rule = Types()
            .That()
            .ResideInAssembly(typeof(SubmitFlag).Assembly)
            .Should()
            .NotDependOnAny(
                typeof(Microsoft.EntityFrameworkCore.DbContext),
                typeof(Marten.IDocumentSession),
                typeof(Wolverine.IMessageBus));

        await Assert.That(rule.HasNoViolations(architecture)).IsTrue();
    }
}
