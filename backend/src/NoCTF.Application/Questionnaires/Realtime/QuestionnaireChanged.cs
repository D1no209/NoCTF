namespace NoCTF.Application.Questionnaires.Realtime;

public sealed record QuestionnaireChanged(Guid ActivityId, Guid? ParticipantId);
public interface IQuestionnaireChangePublisher
{
    Task PublishAsync(QuestionnaireChanged change, CancellationToken ct);
}
