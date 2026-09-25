using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Teams.Registration;

internal static class ParticipantTeamRegistrationTransition
{
    public static async Task ApplyAsync(
        Competition competition,
        Team team,
        Guid actorUserId,
        DateTimeOffset occurredAt,
        ICompetitionEventRecorder events,
        CancellationToken cancellationToken)
    {
        var nextStatus = ParticipantTeamMutationPolicy.RegistrationStatusAfterChange();
        if (team.RegistrationStatus == nextStatus)
            return;

        team.RegistrationStatus = nextStatus;
        var track = CompetitionTrackConfiguration.FromPersisted(
            competition.Mode,
            competition.Tracks).Find(team.TrackKey);
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.TeamRegistrationChanged,
            CompetitionEventLevel.Information,
            nextStatus == TeamRegistrationStatus.Approved && track?.IsInternal != true
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            occurredAt,
            ActorUserId: actorUserId,
            RelatedUserId: actorUserId,
            TeamId: team.Id,
            TeamRegistrationStatus: nextStatus,
            TrackKey: team.TrackKey), cancellationToken);
    }
}
