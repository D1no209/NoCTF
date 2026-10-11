namespace NoCTF.Application.Theory.Realtime;

public sealed record TheoryChanged(Guid ActivityId, Guid? ParticipantId);
public interface ITheoryChangePublisher
{
    Task PublishAsync(TheoryChanged change, CancellationToken ct);
}
