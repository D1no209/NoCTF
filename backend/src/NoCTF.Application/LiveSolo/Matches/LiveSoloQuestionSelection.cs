using System.Security.Cryptography;
using System.Text;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Matches;

public enum LiveSoloQuestionExclusion : short
{
    Empty, MissingQuestion, DeletedQuestion, DuplicateQuestion, InvalidSchedule, UnsupportedDefinition, ActiveRoundConflict
}
public sealed record LiveSoloQuestionRisk(LiveSoloSelectionRiskKind Kind, Guid? CompetitionChallengeId = null, Guid? CanonicalChallengeId = null);
public sealed record LiveSoloQuestionCandidate(Guid Id, string Name, bool Reserve, int Position, Guid ConcurrencyStamp,
    Guid SelectionRevision, IReadOnlyList<LiveSoloQuestionExclusion> Exclusions, IReadOnlyList<LiveSoloQuestionRisk> Risks,
    IReadOnlyList<LiveSoloQuestionGroupEntry> Questions)
{
    public bool Available => Exclusions.Count == 0;
    public bool AutomaticEligible => Available && Risks.Count == 0;
    public IReadOnlyList<LiveSoloQuestionUse> Uses { get; init; } = [];
}
public sealed record LiveSoloQuestionUse(Guid MatchId, Guid RoundId, int Number, int Replay,
    LiveSoloRoundState State, DateTimeOffset PreparedAt, DateTimeOffset? OpenedAt);

public sealed record LiveSoloSelectionMaterial(Guid Id, Guid? CanonicalId, Guid EntryStamp, Guid MaterialStamp,
    bool Exists, bool Deleted, bool Supported, bool Active, bool SeenByTeam, bool SeenByMember, bool Exposed);

public static class LiveSoloQuestionSelection
{
    public static LiveSoloQuestionCandidate Evaluate(Guid id, string name, bool reserve, int position, Guid stamp,
        IReadOnlyList<LiveSoloSelectionMaterial> materials, IReadOnlyList<LiveSoloQuestionGroupEntry> questions,
        bool validSchedule, bool previouslyUsed, int interval, int limit)
    {
        var exclusions = new HashSet<LiveSoloQuestionExclusion>(); var risks = new List<LiveSoloQuestionRisk>();
        if (materials.Count == 0) exclusions.Add(LiveSoloQuestionExclusion.Empty);
        if (materials.Select(x => x.Id).Distinct().Count() != materials.Count) exclusions.Add(LiveSoloQuestionExclusion.DuplicateQuestion);
        if (!validSchedule) exclusions.Add(LiveSoloQuestionExclusion.InvalidSchedule);
        foreach (var material in materials)
        {
            if (!material.Exists) { exclusions.Add(LiveSoloQuestionExclusion.MissingQuestion); continue; }
            if (material.Deleted) exclusions.Add(LiveSoloQuestionExclusion.DeletedQuestion);
            if (!material.Supported) exclusions.Add(LiveSoloQuestionExclusion.UnsupportedDefinition);
            if (material.Active) exclusions.Add(LiveSoloQuestionExclusion.ActiveRoundConflict);
            if (material.SeenByTeam) risks.Add(new(LiveSoloSelectionRiskKind.SeenByTeam, material.Id, material.CanonicalId));
            if (material.SeenByMember) risks.Add(new(LiveSoloSelectionRiskKind.SeenByRosterMember, material.Id, material.CanonicalId));
            if (material.Exposed) risks.Add(new(LiveSoloSelectionRiskKind.PublicExposure, material.Id, material.CanonicalId));
        }
        foreach (var source in materials.Where(x => x.Exists).GroupBy(x => x.CanonicalId).Where(x => x.Count() > 1))
            risks.Add(new(LiveSoloSelectionRiskKind.SameSource, CanonicalChallengeId: source.Key));
        if (previouslyUsed) risks.Add(new(LiveSoloSelectionRiskKind.PreviouslyUsed));
        var reasons = exclusions.Order().ToArray();
        return new(id, name, reserve, position, stamp, Revision(stamp, materials.SelectMany(x => new[] { x.EntryStamp, x.MaterialStamp }), reasons, risks, interval, limit), reasons, risks, questions);
    }

    public static LiveSoloQuestionCandidate? Select(IReadOnlyList<LiveSoloQuestionCandidate> candidates, Guid? requestedId) => requestedId is Guid id
        ? candidates.SingleOrDefault(x => x.Id == id && x.Available)
        : candidates.Where(x => x.AutomaticEligible).OrderBy(x => x.Reserve).ThenBy(x => x.Position).ThenBy(x => x.Id).FirstOrDefault();

    /// <summary>Opaque revision of the exact material and eligibility displayed to the staff member.</summary>
    public static Guid Revision(Guid stamp, IEnumerable<Guid> materialStamps, IEnumerable<LiveSoloQuestionExclusion> exclusions,
        IEnumerable<LiveSoloQuestionRisk> risks, int interval, int limit)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write(stamp.ToByteArray()); writer.Write(interval); writer.Write(limit);
        foreach (var material in materialStamps) writer.Write(material.ToByteArray());
        writer.Write((short)-1);
        foreach (var exclusion in exclusions.Order()) writer.Write((short)exclusion);
        writer.Write((short)-1);
        foreach (var risk in risks.OrderBy(x => x.Kind).ThenBy(x => x.CompetitionChallengeId).ThenBy(x => x.CanonicalChallengeId))
        { writer.Write((short)risk.Kind); writer.Write((risk.CompetitionChallengeId ?? Guid.Empty).ToByteArray()); writer.Write((risk.CanonicalChallengeId ?? Guid.Empty).ToByteArray()); }
        writer.Flush(); return new Guid(SHA256.HashData(bytes.ToArray()).AsSpan(0, 16));
    }
}
