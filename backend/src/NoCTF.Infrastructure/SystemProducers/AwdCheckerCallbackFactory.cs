using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.SystemProducers;

public sealed class AwdCheckerCallbackFactory(IConfiguration configuration) : IAwdCheckerCallbackFactory
{
    public RunnerScoringCallback Create(AwdCheckerTarget target, string sourceKey)
    {
        var baseUrl = configuration["RunnerScoring:CallbackBaseUrl"]
            ?? throw new InvalidOperationException("RunnerScoring:CallbackBaseUrl is required for AWD checker callbacks.");
        var url = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"),
            $"internal/competitions/{target.CompetitionId}/awd-check-results");
        return new(url, configuration["RunnerScoring:RunnerId"] ?? "noctf-runner", new Dictionary<string, string>
        {
            ["teamId"] = target.TeamId.ToString(),
            ["competitionChallengeId"] = target.CompetitionChallengeId.ToString(),
            ["sourceKey"] = sourceKey
        });
    }
}
