using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.Worker.Questionnaires;

public sealed class QuestionnaireActivityMessageHandler(IQuestionnaireSessionStore sessions, IQuestionnaireReviewStore reviews, TimeProvider clock)
{
    public Task Handle(SweepQuestionnaireActivity message, CancellationToken ct) => sessions.SweepAsync(message.ActivityId, clock.GetUtcNow(), ct);
    public Task Handle(ApplyQuestionnaireCorrection message, CancellationToken ct) => reviews.ApplyAsync(message, clock.GetUtcNow(), ct);
}
