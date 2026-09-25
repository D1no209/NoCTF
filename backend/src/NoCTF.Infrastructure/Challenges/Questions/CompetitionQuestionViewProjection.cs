using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Notifications;
using static NoCTF.Infrastructure.Challenges.Questions.CompetitionQuestionStore;

namespace NoCTF.Infrastructure.Challenges.Questions;

internal static class CompetitionQuestionViewProjection
{
    internal static CompetitionQuestionView Build(
        QuestionAggregate question,
        CompetitionQuestionAccess access,
        bool includeEntries,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> teamNames,
        IReadOnlyDictionary<Guid, string> challengeTitles,
        int maximumParticipantMessages)
    {
        var askerId = question.RootNotification.SourceId!.Value;
        var rootActorRole = question.Root.ActorRole
            ?? CompetitionQuestionParticipantRole.Participant;
        var lastNode = question.Nodes.LastOrDefault();
        var lastActorId = lastNode?.SourceId ?? askerId;
        var lastActorRole = lastNode is null
            ? rootActorRole
            : ReadActorRole(lastNode);
        var remainingParticipantMessages = Math.Max(
            0,
            maximumParticipantMessages
                - CountParticipantMessagesSinceHandlerReply(question));
        var entries = includeEntries
            ? question.Nodes.Select(node => MapEntry(node, names))
                .Prepend(new CompetitionQuestionEntryView(
                    question.RootNotification.Id,
                    CompetitionQuestionEntryKind.Message,
                    rootActorRole,
                    askerId,
                    names.GetValueOrDefault(askerId, "已删除用户"),
                    question.Root.Body,
                    null,
                    null,
                    null,
                    question.RootNotification.SentAt))
                .ToArray()
            : [];
        return new(
            question.RootNotification.Id,
            question.RootNotification.TargetId,
            question.Root.CompetitionChallengeId,
            question.Root.TeamId,
            askerId,
            names.GetValueOrDefault(askerId, "已删除用户"),
            teamNames.GetValueOrDefault(question.Root.TeamId),
            question.Root.GameplayFactId,
            question.Root.Subject,
            question.Root.CompetitionChallengeId is { } challengeId
                ? challengeTitles.GetValueOrDefault(challengeId)
                : null,
            question.Root.Title,
            question.Root.Body,
            question.Status,
            access,
            names.GetValueOrDefault(lastActorId, "已删除用户"),
            lastActorRole,
            remainingParticipantMessages,
            maximumParticipantMessages,
            question.RootNotification.SentAt,
            question.Nodes.Count == 0 ? question.RootNotification.SentAt : question.Nodes[^1].SentAt,
            entries);
    }

    internal static int CountParticipantMessagesSinceHandlerReply(QuestionAggregate question)
    {
        var messages = question.Nodes
            .Where(node => node.Kind == NotificationKind.Message)
            .ToArray();
        var lastHandlerIndex = Array.FindLastIndex(
            messages,
            message => CompetitionQuestionRules.IsHandler(
                message.QuestionActorRole!.Value));
        var participantMessages = messages
            .Skip(lastHandlerIndex + 1)
            .Count(message => CompetitionQuestionRules.IsParticipant(
                message.QuestionActorRole!.Value));

        var rootIsParticipant = CompetitionQuestionRules.IsParticipant(
            question.Root.ActorRole ?? CompetitionQuestionParticipantRole.Participant);
        return lastHandlerIndex < 0 && rootIsParticipant
            ? participantMessages + 1
            : participantMessages;
    }

    private static CompetitionQuestionEntryView MapEntry(
        Notification node,
        IReadOnlyDictionary<Guid, string> names)
    {
        var actorId = node.SourceId ?? Guid.Empty;
        if (node.Kind == NotificationKind.Message)
        {
            return new(node.Id, CompetitionQuestionEntryKind.Message,
                node.QuestionActorRole!.Value,
                node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), node.Body,
                node.PreviousQuestionStatus, node.QuestionStatus, null, node.SentAt);
        }
        return new(node.Id, CompetitionQuestionEntryKind.StatusTransition,
            node.QuestionActorRole!.Value,
            node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), null,
            node.PreviousQuestionStatus, node.QuestionStatus, null, node.SentAt);
    }

    internal static CompetitionQuestionParticipantRole ReadActorRole(Notification node) =>
        node.QuestionActorRole
            ?? throw new InvalidOperationException(
                $"Question node {node.Id} has no actor role.");
}
