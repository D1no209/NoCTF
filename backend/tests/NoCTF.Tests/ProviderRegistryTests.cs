using NoCTF.Application.CompetitionModes;

namespace NoCTF.Tests;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void CompetitionModeRegistry_RejectsCaseInsensitiveDuplicateKeys()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CompetitionModeRegistry([
                new TestModeProvider("ctf"),
                new TestModeProvider(" CTF ")
            ]));

        Assert.Contains("Duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompetitionModeRegistry_UsesTrimmedCaseInsensitiveLookup()
    {
        var provider = new TestModeProvider("ctf");
        var registry = new CompetitionModeRegistry([provider]);

        Assert.Same(provider, registry.GetRequiredProvider(" CTF "));
    }

    [Fact]
    public void CompetitionFileActionRegistry_RejectsAmbiguousActionHandlers()
    {
        var registry = new CompetitionFileActionRegistry([
            new TestFileActionProvider("awdp", "submit-patch"),
            new TestFileActionProvider("AWDP", "submit-patch")
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            registry.FindProvider("awdp", "submit-patch"));
    }

    private sealed class TestModeProvider(string modeKey) : ICompetitionModeProvider
    {
        public string ModeKey => modeKey;

        public CompetitionCapabilityDescriptor GetCapabilities()
            => new(ModeKey, [], [], []);

        public bool CanHandleAction(string actionKey) => false;

        public Task<CompetitionActionResult> HandleActionAsync(
            CompetitionActionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new CompetitionActionResult(false, "unsupported"));

        public bool CanProvideView(string viewKey) => false;

        public Task<CompetitionViewResult> GetViewAsync(
            CompetitionViewContext context,
            CancellationToken ct = default)
            => Task.FromResult(new CompetitionViewResult(context.ViewKey, new { }));
    }

    private sealed class TestFileActionProvider(string modeKey, string actionKey)
        : ICompetitionFileActionProvider
    {
        public string ModeKey => modeKey;

        public bool CanHandleFileAction(string candidate)
            => string.Equals(candidate, actionKey, StringComparison.OrdinalIgnoreCase);

        public Task<CompetitionActionResult> HandleFileActionAsync(
            CompetitionFileActionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new CompetitionActionResult(true, "ok"));
    }
}
