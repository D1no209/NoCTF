using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Observability;
using NoCTF.Hosting.Observability;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ObservabilityExtensionsTests
{
    [Test]
    [Arguments("NoCTF.Infrastructure.Persistence.DatabaseStartup", LogLevel.Information, true)]
    [Arguments("NoCTF.Infrastructure.Persistence.DatabaseStartup", LogLevel.Debug, false)]
    [Arguments("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning, true)]
    [Arguments("Microsoft.EntityFrameworkCore.Update", LogLevel.Error, true)]
    [Arguments("Microsoft.EntityFrameworkCore.Query", LogLevel.Information, false)]
    [Arguments(null, LogLevel.Warning, true)]
    public async Task Platform_log_filter_keeps_application_information_and_all_warnings(
        string? category,
        LogLevel level,
        bool expected)
    {
        var exported = ObservabilityExtensions.ShouldExportPlatformLog(category, level);

        await Assert.That(exported).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/metrics", 9464, 9464, true)]
    [Arguments("/metrics", 8080, 9464, false)]
    [Arguments("/health/ready", 9464, 9464, false)]
    [Arguments("/metrics/details", 9464, 9464, false)]
    public async Task Metrics_scraping_is_restricted_to_the_private_listener(
        string path,
        int localPort,
        int metricsPort,
        bool expected)
    {
        var matches = ObservabilityExtensions.IsMetricsScrapeRequest(
            new PathString(path),
            localPort,
            metricsPort);

        await Assert.That(matches).IsEqualTo(expected);
    }

    [Test]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions", RuntimeOperationMetricKind.Flag)]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement", RuntimeOperationMetricKind.Flag)]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets", RuntimeOperationMetricKind.FixRequest)]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix", RuntimeOperationMetricKind.FixUpload)]
    public async Task Mutating_gameplay_routes_are_classified(
        string method,
        string route,
        RuntimeOperationMetricKind expected)
    {
        var operation = ObservabilityExtensions.ClassifyGameplayOperation(method, route);

        await Assert.That(operation).IsEqualTo(expected);
    }

    [Test]
    [Arguments("GET", "/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime")]
    [Arguments("GET", "/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions")]
    [Arguments("POST", "/admin/challenges/{challengeId}/flags")]
    [Arguments("POST", "/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/flag-access")]
    public async Task Read_and_administration_routes_are_not_gameplay_operations(
        string method,
        string route)
    {
        var operation = ObservabilityExtensions.ClassifyGameplayOperation(method, route);

        await Assert.That(operation).IsNull();
    }
}
