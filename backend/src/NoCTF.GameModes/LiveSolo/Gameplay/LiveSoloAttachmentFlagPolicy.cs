using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.GameModes.LiveSolo.Gameplay;

public static class LiveSoloAttachmentFlagPolicy
{
    public static IReadOnlyList<ChallengeFlag> Bind(IReadOnlyList<ChallengeFlag> candidates,
        IReadOnlyList<LiveSoloAttachmentAssignment> assignments, Guid questionId, Guid entryId, Guid actorTeamId, DateTimeOffset admittedAt)
    {
        var result = new List<ChallengeFlag>();
        foreach (var flag in candidates)
        {
            if (flag.SpecificationKind != SpecificationKind.Attachment) { result.Add(flag); continue; }
            var owners = assignments.Where(x => x.RoundQuestionId == questionId && x.AttachmentId == flag.SpecificationId && x.AssignedAt <= admittedAt)
                .Select(x => x.TeamId).Distinct().ToArray();
            if (owners.Contains(actorTeamId)) owners = [actorTeamId];
            if (owners.Length == 0) { result.Add(flag); continue; }
            foreach (var owner in owners)
                result.Add(new TeamChallengeFlag { Id = flag.Id, CompetitionChallengeId = entryId, TeamId = owner, Flag = flag.Flag,
                    FlagSha256 = flag.FlagSha256, MatchKind = flag.MatchKind, SpecificationKind = SpecificationKind.Attachment,
                    SpecificationId = flag.SpecificationId, ValidStart = flag.ValidStart, ValidUntil = flag.ValidUntil });
        }
        return result;
    }
}
