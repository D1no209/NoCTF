using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Infrastructure.Runtime.Configuration;

public static class RuntimeExecutionConfiguration
{
    public static IServiceCollection AddNoCtfRuntimeExecutionOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RuntimeExecutionOptions>().Bind(configuration.GetSection("Runtime:Execution"))
            .Validate(options => options.ProcessesPerService > 0, "ProcessesPerService must be positive.").ValidateOnStart();
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<RuntimeExecutionOptions>>().Value);
        return services;
    }
}
