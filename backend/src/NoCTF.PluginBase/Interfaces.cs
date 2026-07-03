using Microsoft.Extensions.DependencyInjection;

namespace NoCTF.PluginBase;

public interface IGameMode
{
    GameModeType Type { get; }
    Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default);
    Task<SubmissionResult> ProcessSubmissionAsync(SubmissionContext context, CancellationToken cancellationToken = default);
    Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default);
}

public interface IChallengeType
{
    string TypeId { get; }
    Task<ValidationResult> ValidateFlagAsync(string submittedFlag, ChallengeContext context, CancellationToken cancellationToken = default);
    ContainerConfig? GetContainerConfig(ChallengeContext context);
}

public interface IContainerManager
{
    Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default);
    Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default);
    Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default);
    Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default);
    Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default);
}

public interface IContainerProvider<TClient, TMetadata>
{
    TClient CreateClient();
    Task<TMetadata> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default);
    Task DestroyContainerAsync(TMetadata metadata, CancellationToken cancellationToken = default);
}

public interface IStorageProvider
{
    Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);
    Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default);
}

public interface IPluginModule
{
    string Name { get; }
    string Version { get; }
    void ConfigureServices(IServiceCollection services);
}
