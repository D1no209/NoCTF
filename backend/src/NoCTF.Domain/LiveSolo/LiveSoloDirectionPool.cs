using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public sealed class LiveSoloDirectionPreset : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    [Required, MaxLength(96)] public string Direction { get; set; } = "";
    public Guid QuestionGroupId { get; set; }
}

public sealed class LiveSoloMatchDirection
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int Position { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    [Required, MaxLength(96)] public string Direction { get; set; } = "";
    public Guid QuestionGroupId { get; set; }
    public int? RoundNumber { get; set; }
    public DateTimeOffset? DrawnAt { get; set; }
}

public sealed class LiveSoloRoundRosterMember
{
    public Guid RoundId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public enum LiveSoloRescueState : short { Pending, Approved, Rejected }

public sealed class LiveSoloRescueRequest : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [Required, MaxLength(4000)] public string Reason { get; set; } = "";
    public LiveSoloRescueState State { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? JudgeUserId { get; set; }
    [MaxLength(4000)] public string? DecisionReason { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
