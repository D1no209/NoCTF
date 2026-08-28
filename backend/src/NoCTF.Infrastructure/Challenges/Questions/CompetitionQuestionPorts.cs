using NoCTF.Application.Challenges.Questions;

namespace NoCTF.Infrastructure.Challenges.Questions;

internal sealed class CompetitionQuestionQueryReader(CompetitionQuestionStore store)
    : ICompetitionQuestionReader
{
    public Task<CompetitionQuestionPage> ListAsync(
        CompetitionQuestionQuery query,
        CancellationToken cancellationToken) =>
        store.ListAsync(query, cancellationToken);

    public Task<CompetitionQuestionView?> FindAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        store.FindAsync(competitionId, questionId, actorUserId, cancellationToken);
}

internal sealed class CompetitionQuestionTransactionWriter(CompetitionQuestionStore store)
    : ICompetitionQuestionWriter
{
    public Task<CompetitionQuestionMutationResult> CreateAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken cancellationToken) =>
        store.CreateAsync(command, cancellationToken);

    public Task<CompetitionQuestionMutationResult> AddMessageAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken cancellationToken) =>
        store.AddMessageAsync(command, cancellationToken);

    public Task<CompetitionQuestionMutationResult> ChangeStatusAsync(
        ChangeCompetitionQuestionStatusCommand command,
        CancellationToken cancellationToken) =>
        store.ChangeStatusAsync(command, cancellationToken);
}
