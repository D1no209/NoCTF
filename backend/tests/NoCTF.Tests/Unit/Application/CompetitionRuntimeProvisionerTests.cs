using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionRuntimeProvisionerTests
{
    [Test]
    public async Task ExecuteAsync_PerTeamTemplate_ProvisionsEachApprovedTeamWithReservedIdentity()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teams = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var snapshot = Snapshot(competitionId, challengeId, teams);
        var operations = new Operations();
        var runtime = new Runtime();
        var template = Template(RuntimeAllocation.PerTeam) with
        {
            Environment = new Dictionary<string, string> { ["NOCTF_COMPETITION_ID"] = "untrusted" }
        };
        var provisioner = Create(snapshot, template, operations, runtime);

        var result = await provisioner.ExecuteAsync(competitionId);

        await Assert.That(result.AttemptedCount).IsEqualTo(2);
        await Assert.That(result.ProvisionedCount).IsEqualTo(2);
        await Assert.That(runtime.Requests).Count().IsEqualTo(2);
        await Assert.That(runtime.Requests.Select(request => request.Environment["NOCTF_TEAM_ID"]))
            .IsEquivalentTo(teams.Select(team => team.ToString("N")));
        await Assert.That(runtime.Requests[0].Environment["NOCTF_COMPETITION_ID"])
            .IsEqualTo(competitionId.ToString("N"));
        await Assert.That(runtime.Requests[0].Labels["noctf.io/competition-id"])
            .IsEqualTo(competitionId.ToString("N"));
        await Assert.That(operations.OperationKeys.Distinct()).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_SharedTemplate_ProvisionsSingleRuntimeWithoutTeamIdentity()
    {
        var competitionId = Guid.NewGuid();
        var snapshot = Snapshot(competitionId, Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()]);
        var runtime = new Runtime();
        var provisioner = Create(snapshot, Template(RuntimeAllocation.Shared), new Operations(), runtime);

        var result = await provisioner.ExecuteAsync(competitionId);

        await Assert.That(result.AttemptedCount).IsEqualTo(1);
        await Assert.That(runtime.Requests).HasSingleItem();
        await Assert.That(runtime.Requests[0].Environment.ContainsKey("NOCTF_TEAM_ID")).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_NonRunningCompetition_DoesNotProvision()
    {
        var competitionId = Guid.NewGuid();
        var snapshot = Snapshot(competitionId, Guid.NewGuid(), [Guid.NewGuid()]) with
        {
            Status = CompetitionStatus.Paused
        };
        var runtime = new Runtime();
        var provisioner = Create(snapshot, Template(RuntimeAllocation.PerTeam), new Operations(), runtime);

        var result = await provisioner.ExecuteAsync(competitionId);

        await Assert.That(result.AttemptedCount).IsEqualTo(0);
        await Assert.That(runtime.Requests).IsEmpty();
    }

    private static CompetitionRuntimeProvisioner Create(
        CompetitionRuntimeProvisioningSnapshot snapshot,
        ChallengeRuntimeTemplate template,
        Operations operations,
        Runtime runtime) =>
        new(
            new Store(snapshot),
            new Templates(template),
            new ChallengeRuntimeProvisioner(operations, runtime));

    private static CompetitionRuntimeProvisioningSnapshot Snapshot(
        Guid competitionId,
        Guid challengeId,
        IReadOnlyList<Guid> teams) =>
        new(
            competitionId,
            GameMode.Awd,
            CompetitionStatus.Running,
            [new(challengeId, 1, 3, "{}")],
            teams);

    private static ChallengeRuntimeTemplate Template(RuntimeAllocation allocation) => new(
        RuntimeProvider.Kubernetes,
        allocation,
        "challenge:v1",
        ["/challenge"],
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["noctf.io/competition-id"] = "untrusted" },
        new Dictionary<int, int> { [8080] = 0 },
        new(268_435_456, 500_000_000, 128),
        new(true, true, true, ["ALL"], []),
        3600,
        30);

    private sealed class Store(CompetitionRuntimeProvisioningSnapshot snapshot)
        : ICompetitionRuntimeProvisioningStore
    {
        public Task<CompetitionRuntimeProvisioningSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionRuntimeProvisioningSnapshot?>(snapshot);
    }

    private sealed class Templates(ChallengeRuntimeTemplate template) : IChallengeRuntimeTemplateCatalog
    {
        public ChallengeRuntimeTemplate? Get(GameMode mode, string challengeConfigurationJson) => template;
    }

    private sealed class Operations : IRuntimeOperationStore
    {
        public List<string> OperationKeys { get; } = [];

        public Task<RuntimeOperationBeginResult> BeginAsync(
            Guid competitionId,
            string operationKey,
            RuntimeOperationKind kind,
            DateTimeOffset staleBefore,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            OperationKeys.Add(operationKey);
            return Task.FromResult(new RuntimeOperationBeginResult(new RuntimeOperationLease(
                Guid.CreateVersion7(now), competitionId, operationKey, Guid.NewGuid(), RuntimeStatus.Pending, true)));
        }

        public Task<bool> CompleteAsync(
            RuntimeOperationLease lease,
            ContainerReceipt receipt,
            Guid challengeId,
            Guid? teamId,
            DateTimeOffset? expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<RuntimeOperationFailureResult> FailAsync(
            RuntimeOperationLease lease,
            RuntimeOperationFailureContext failure,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(new RuntimeOperationFailureResult());
    }

    private sealed class Runtime : IContainerLifecycle
    {
        public List<ContainerRequest> Requests { get; } = [];

        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ContainerReceipt(
                request.OperationId,
                request.Provider,
                $"resource-{Requests.Count}",
                RuntimeStatus.Running,
                request.PortMappings,
                "runtime.local",
                null));
        }

        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken) => Task.FromResult<ContainerReceipt?>(null);
    }
}
