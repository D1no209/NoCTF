using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Notifications;
using static NoCTF.Infrastructure.Challenges.Questions.CompetitionQuestionStore;

namespace NoCTF.Infrastructure.Challenges.Questions;

internal static class CompetitionQuestionSerialization
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

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
        var lastNode = question.Nodes.LastOrDefault();
        var lastActorId = lastNode?.SourceId ?? askerId;
        var lastActorRole = lastNode is null
            ? CompetitionQuestionParticipantRole.Participant
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
                    CompetitionQuestionParticipantRole.Participant,
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
            .Select(node => JsonSerializer.Deserialize<QuestionMessagePayload>(
                node.ContentJson,
                CompetitionQuestionSerialization.Options)!)
            .ToArray();
        var lastHandlerIndex = Array.FindLastIndex(
            messages,
            message => CompetitionQuestionRules.IsHandler(message.ActorRole));
        var participantMessages = messages
            .Skip(lastHandlerIndex + 1)
            .Count(message => CompetitionQuestionRules.IsParticipant(message.ActorRole));

        // The root notification is the participant's first message.
        return lastHandlerIndex < 0 ? participantMessages + 1 : participantMessages;
    }

    private static CompetitionQuestionEntryView MapEntry(
        Notification node,
        IReadOnlyDictionary<Guid, string> names)
    {
        var actorId = node.SourceId ?? Guid.Empty;
        if (node.Kind == NotificationKind.Message)
        {
            var payload = JsonSerializer.Deserialize<QuestionMessagePayload>(
                node.ContentJson,
                CompetitionQuestionSerialization.Options)!;
            return new(node.Id, CompetitionQuestionEntryKind.Message, payload.ActorRole,
                node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), payload.Body,
                payload.From, payload.To, null, node.SentAt);
        }
        var status = JsonSerializer.Deserialize<QuestionStatusPayload>(
            node.ContentJson,
            CompetitionQuestionSerialization.Options)!;
        return new(node.Id, CompetitionQuestionEntryKind.StatusTransition, status.ActorRole,
            node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), null,
            status.From, status.To, null, node.SentAt);
    }

    internal static CompetitionQuestionParticipantRole ReadActorRole(Notification node) =>
        node.Kind == NotificationKind.Message
            ? JsonSerializer.Deserialize<QuestionMessagePayload>(
                node.ContentJson,
                CompetitionQuestionSerialization.Options)!.ActorRole
            : JsonSerializer.Deserialize<QuestionStatusPayload>(
                node.ContentJson,
                CompetitionQuestionSerialization.Options)!.ActorRole;
}
