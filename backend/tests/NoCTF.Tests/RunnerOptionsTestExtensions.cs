using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;

namespace Microsoft.Extensions.Configuration;

internal static class RunnerOptionsTestExtensions
{
    internal static IOptions<RunnerOptions> ToRunnerOptions(
        this IConfiguration configuration)
    {
        _ = Enum.TryParse<RuntimeProvider>(
            configuration["Runner:Provider"],
            ignoreCase: true,
            out var provider);
        return Microsoft.Extensions.Options.Options.Create(new RunnerOptions
        {
            Id = configuration["Runner:Id"] ?? "runner-a",
            Pool = configuration["Runner:Pool"] ?? "default",
            Provider = provider == default && configuration["Runner:Provider"] is null
                ? RuntimeProvider.Docker
                : provider
        });
    }
}
