namespace NoCTF.Domain.Competitions.Events;

public static class ScoreboardChangePolicy
{
    public static bool ChangesAccess(CompetitionEventKind kind) => kind is
        CompetitionEventKind.CompetitionCreated or CompetitionEventKind.CompetitionUpdated
        or CompetitionEventKind.CompetitionDeleted or CompetitionEventKind.CompetitionLifecycleChanged
        or CompetitionEventKind.LeaderboardVisibilityChanged or CompetitionEventKind.CompetitionAudienceChanged
        or CompetitionEventKind.ChallengeCreated or CompetitionEventKind.ChallengeUpdated
        or CompetitionEventKind.ChallengePublished or CompetitionEventKind.ChallengeUnpublished
        or CompetitionEventKind.ChallengeDeleted or CompetitionEventKind.ChallengeDescriptionUpdated
        or CompetitionEventKind.TeamRegistered or CompetitionEventKind.TeamRegistrationChanged
        or CompetitionEventKind.TeamUpdated or CompetitionEventKind.TeamDeleted
        or CompetitionEventKind.TeamBanned or CompetitionEventKind.TeamUnbanned
        or CompetitionEventKind.TeamBanCorrectionPublished or CompetitionEventKind.TeamMemberRemoved
        or CompetitionEventKind.TeamMemberJoined or CompetitionEventKind.TeamCaptainTransferred
        or CompetitionEventKind.TrackConfigurationUpdated or CompetitionEventKind.TeamTrackChanged;
}
