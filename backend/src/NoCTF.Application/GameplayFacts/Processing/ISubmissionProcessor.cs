namespace NoCTF.Application.GameplayFacts.Processing;

public interface IGameplayFactProcessor
{
    Task ProcessAsync(Guid gameplayFactId, CancellationToken cancellationToken);
}
