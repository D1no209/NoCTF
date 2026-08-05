using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionQuestionRulesTests
{
    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    public async Task Creation_AllowsApprovedHumanDuringActiveCompetition(
        CompetitionStatus status)
    {
        var result = CompetitionQuestionRules.ValidateCreation(new(
            IsHuman: true,
            CompetitionStatus: status,
            HasApprovedTeam: true,
            Subject: CompetitionQuestionSubject.Challenge,
            HasValidChallenge: true,
            HasValidSubmission: true));

        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments(CompetitionStatus.Draft)]
    [Arguments(CompetitionStatus.Published)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task Creation_RejectsNonActiveCompetition(CompetitionStatus status)
    {
        var result = CompetitionQuestionRules.ValidateCreation(new(
            true,
            status,
            true,
            CompetitionQuestionSubject.Platform,
            true,
            true));

        await Assert.That(result)
            .IsEqualTo(CompetitionQuestionFailure.LifecycleConflict);
    }

    [Test]
    public async Task Creation_RequiresChallengeOnlyForChallengeSubject()
    {
        var challengeFailure = CompetitionQuestionRules.ValidateCreation(new(
            true,
            CompetitionStatus.Running,
            true,
            CompetitionQuestionSubject.Challenge,
            false,
            true));
        var platformFailure = CompetitionQuestionRules.ValidateCreation(new(
            true,
            CompetitionStatus.Running,
            true,
            CompetitionQuestionSubject.Platform,
            false,
            true));

        await Assert.That(challengeFailure)
            .IsEqualTo(CompetitionQuestionFailure.ChallengeNotFound);
        await Assert.That(platformFailure).IsNull();
    }

    [Test]
    [Arguments(CompetitionQuestionStatus.Pending, CompetitionQuestionParticipantRole.Handler, CompetitionQuestionStatus.Replied)]
    [Arguments(CompetitionQuestionStatus.Replied, CompetitionQuestionParticipantRole.Asker, CompetitionQuestionStatus.Pending)]
    [Arguments(CompetitionQuestionStatus.Resolved, CompetitionQuestionParticipantRole.Asker, CompetitionQuestionStatus.Pending)]
    public async Task Message_DerivesStrongStatus(
        CompetitionQuestionStatus current,
        CompetitionQuestionParticipantRole actor,
        CompetitionQuestionStatus expected)
    {
        var result = CompetitionQuestionRules.StatusAfterMessage(current, actor);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ClosedQuestion_IsTerminal()
    {
        await Assert.That(CompetitionQuestionRules.StatusAfterMessage(
                CompetitionQuestionStatus.Closed,
                CompetitionQuestionParticipantRole.Handler))
            .IsNull();
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Closed,
                CompetitionQuestionStatus.Pending,
                CompetitionQuestionParticipantRole.Handler))
            .IsFalse();
    }

    [Test]
    public async Task Handler_CannotReopenResolvedQuestionByReplying()
    {
        await Assert.That(CompetitionQuestionRules.StatusAfterMessage(
                CompetitionQuestionStatus.Resolved,
                CompetitionQuestionParticipantRole.Handler))
            .IsNull();
    }

    [Test]
    public async Task Asker_CanResolveAReplyButCannotCloseQuestion()
    {
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Replied,
                CompetitionQuestionStatus.Resolved,
                CompetitionQuestionParticipantRole.Asker))
            .IsTrue();
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Replied,
                CompetitionQuestionStatus.Closed,
                CompetitionQuestionParticipantRole.Asker))
            .IsFalse();
    }

    [Test]
    public async Task Handler_CanResolveOrCloseButCannotReopenClosedQuestion()
    {
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Pending,
                CompetitionQuestionStatus.Resolved,
                CompetitionQuestionParticipantRole.Handler))
            .IsTrue();
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Replied,
                CompetitionQuestionStatus.Closed,
                CompetitionQuestionParticipantRole.Handler))
            .IsTrue();
        await Assert.That(CompetitionQuestionRules.CanTransition(
                CompetitionQuestionStatus.Closed,
                CompetitionQuestionStatus.Pending,
                CompetitionQuestionParticipantRole.Handler))
            .IsFalse();
    }
}
