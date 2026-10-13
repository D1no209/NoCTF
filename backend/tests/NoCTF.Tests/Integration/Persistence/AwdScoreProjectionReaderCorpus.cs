using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdScoreProjectionReaderPersistenceTests
{
    private static async Task<ProjectionFixture> SeedAsync(DbContextOptions<NoCtfDbContext> options,
        ProjectionCorpusShape shape, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var sequence = 0;
        Guid Id() => Guid.Parse($"00000000-0000-0000-0000-{++sequence:D12}");
        var random = new Random(19437);
        var start = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var pauseSeconds = shape.Rounds / 2 * 60;
        DateTimeOffset Wall(long ticks, bool beginning = false) => start.AddTicks(ticks)
            .AddSeconds(ticks > TimeSpan.FromSeconds(pauseSeconds).Ticks
                || beginning && ticks == TimeSpan.FromSeconds(pauseSeconds).Ticks ? 20 : 0);
        var totalTicks = TimeSpan.FromSeconds(shape.Rounds * 60 - 15).Ticks;
        var end = Wall(totalTicks);
        var users = Enumerable.Range(0, shape.Teams).Select(index => new User
        {
            Id = Id(), UserName = $"projection-{index}", NormalizedUserName = $"PROJECTION-{index}",
            Email = $"projection-{index}@example.test", PasswordHash = "synthetic-only", CreatedAt = start, UpdatedAt = start
        }).ToArray();
        db.Users.AddRange(users);
        var competitionId = Id();
        var configuration = new AwdCompetitionModeConfiguration
        {
            CompetitionId = competitionId, HardeningDurationSeconds = 0, RoundDurationSeconds = 60,
            AwdOptions = new() { TotalRounds = shape.Rounds, SubmissionTailSeconds = 10 },
            TieBreakers = AwdTieBreakPolicy.Default.Select((criterion, position) => new AwdCompetitionTieBreak
                { Position = position, Field = criterion.Field, Direction = criterion.Direction }).ToList()
        };
        var competition = new AwdCompetition
        {
            Id = competitionId, OwnerId = users[0].Id, Title = "Full-history projection corpus",
            Description = new string('d', 2048), ModeConfiguration = configuration,
            StartAt = start, EndAt = end, Status = CompetitionStatus.Finished, CreatedAt = start, UpdatedAt = end,
            Collaborators = users.Take(3).Select(user => new CompetitionCollaborator
                { UserId = user.Id, Role = CompetitionCollaboratorRole.Judge }).ToList(),
            Tracks = Enumerable.Range(0, 3).Select(index => new CompetitionTrackDefinition(
                index == 0 ? "default" : $"track-{index}", $"Track {index}", index == 0, true,
                index == 2, true, false, false, true, true) { Position = index }).ToList(),
            WebhookTargets = Enumerable.Range(0, 2).Select(index => new CompetitionWebhookTarget
                { Id = Id(), Name = $"Disabled {index}", EndpointUrl = $"https://example.test/{index}",
                    Enabled = false, CurrentSecretCiphertext = [1, 2, 3], CreatedAt = start, UpdatedAt = start }).ToList()
        };
        db.Competitions.Add(competition);
        var teams = users.Select((user, index) => new Team
        {
            Id = Id(), CompetitionId = competitionId, CaptainId = user.Id, MemberIds = [user.Id], Name = $"Team {index}",
            InvitationToken = Id().ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = start.AddMinutes(-1), IsBanned = index == shape.Teams - 1
        }).ToArray();
        db.Teams.AddRange(teams);
        var challenges = Enumerable.Range(0, shape.Challenges).Select(index =>
        {
            var templateId = Id();
            var definition = new AwdChallengeDefinition
            {
                ChallengeId = templateId,
                FlagStores = Enumerable.Range(0, 2).Select(store => new AwdFlagStore
                    { Id = Id(), Position = store, Name = $"store-{store}", DeliveryMode = AwdFlagDeliveryMode.BusinessChecker }).ToList()
            };
            db.Challenges.Add(new AwdChallenge { Id = templateId, OwnerId = users[0].Id,
                Title = $"Question {index}", Definition = definition, CreatedAt = start, UpdatedAt = start });
            var entry = new AwdCompetitionChallenge
            {
                Id = Id(), CompetitionId = competitionId, ChallengeId = templateId,
                Rules = new AwdCompetitionChallengeRules(), IsPublished = true, Order = index, UpdatedAt = start
            };
            db.CompetitionChallenges.Add(entry);
            return (Entry: entry, Definition: definition);
        }).ToArray();
        void Lifecycle(CompetitionStatus from, CompetitionStatus to, DateTimeOffset at) =>
            db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent
            {
                Id = Id(), CompetitionId = competitionId, PreviousCompetitionStatus = from,
                CompetitionStatus = to, OccurredAt = at
            });
        Lifecycle(CompetitionStatus.Published, CompetitionStatus.Running, start);
        Lifecycle(CompetitionStatus.Running, CompetitionStatus.Paused, start.AddSeconds(pauseSeconds));
        Lifecycle(CompetitionStatus.Paused, CompetitionStatus.Running, start.AddSeconds(pauseSeconds + 20));
        Lifecycle(CompetitionStatus.Running, CompetitionStatus.Finished, end);
        db.AwdFlagKeys.Add(new() { Id = Id(), CompetitionId = competitionId, Version = 1, ProtectedSecret = [1], CreatedAt = start });
        var rounds = Enumerable.Range(1, shape.Rounds).Select(number =>
        {
            var activeStart = TimeSpan.FromSeconds((number - 1) * 60).Ticks;
            var activeEnd = Math.Min(TimeSpan.FromSeconds(number * 60).Ticks, totalTicks);
            return new AwdRound
            {
                Id = Id(), CompetitionId = competitionId, Number = number,
                StartsAt = Wall(activeStart, true), EndsAt = Wall(activeEnd),
                ActiveStartTicks = activeStart, ActiveEndTicks = activeEnd, SubmissionTailSeconds = 10,
                Partial = number == shape.Rounds,
                TieBreakers = AwdTieBreakPolicy.Default.Select((criterion, position) => new AwdRoundTieBreak
                    { Position = position, Field = criterion.Field, Direction = criterion.Direction }).ToList()
            };
        }).ToArray();
        db.AwdRounds.AddRange(rounds);
        var rules = new List<AwdRoundRules>();
        var issuesByTarget = new Dictionary<(Guid ChallengeId, Guid TeamId), List<AwdFlagIssue>>();
        var checkCount = 0;
        var evidenceCount = 0;
        var issueCount = 0;
        var captureCount = 0;
        foreach (var round in rounds)
        foreach (var (challenge, definition) in challenges)
        {
            var rule = new AwdRoundRules
            {
                Id = Id(), CompetitionId = competitionId, RoundId = round.Id, CompetitionChallengeId = challenge.Id,
                Revision = 1, EffectiveAt = round.StartsAt, AttackPoints = 50, VictimDefensePoolPoints = 100,
                ServiceHealthyPoints = 100, CheckerIntervalSeconds = 30, EligibleTeamCount = teams.Length,
                AttackRewardMode = (AwdAttackRewardMode)(challenge.Order % 4),
                FlagHeader = "synthetic", FlagBodyTemplate = "[SECURE]",
                Options = new()
                {
                    SlaSettlement = (AwdSlaSettlement)(challenge.Order % 3), SlaBasis = AwdSlaBasis.ReadableFlags,
                    DefenseMode = challenge.Order % 4 == 1 ? AwdDefenseMode.SharedPool : AwdDefenseMode.FirstLeak,
                    AttackSettlement = AwdAttackSettlement.FlagExpiry, FlagLifetime = 2, HistoricalLifetime = 2,
                    ConfidentialityPoints = 15, ConsecutiveFailureMultiplier = 1.25m, MaximumRoundPenalty = 50,
                    PenaltyMode = AwdPenaltyMode.Fixed, OfflinePenalty = 10, MumblePenalty = 5,
                    Composition = (AwdScoreComposition)(challenge.Order % 3)
                },
                Teams = teams.Select((team, index) => new AwdRoundTeamEligibility
                    { TeamId = team.Id, Competitive = index != teams.Length - 1, SettledRank = index + 1 }).ToList(),
                Stores = definition.FlagStores.Select((store, index) => new AwdRoundStoreBudget
                {
                    StoreId = store.Id, Name = store.Name, Weight = index + 1, FlagsPerIssue = 2,
                    DeliveryMode = AwdFlagDeliveryMode.BusinessChecker,
                    InjectionCommand = "unused-by-scoring " + new string('x', 256), TargetServiceName = "main"
                }).ToList()
            };
            rules.Add(rule);
            db.AwdRoundRules.Add(rule);
            foreach (var (team, teamIndex) in teams.Select((team, index) => (team, index)))
            {
                var key = (challenge.Id, team.Id);
                if (!issuesByTarget.TryGetValue(key, out var history)) issuesByTarget[key] = history = [];
                foreach (var store in rule.Stores)
                for (var ordinal = 0; ordinal < store.FlagsPerIssue; ordinal++)
                {
                    var flagId = Id();
                    var value = $"synthetic{{{flagId:N}}}";
                    var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
                    var protectedContext = new byte[384];
                    random.NextBytes(protectedContext);
                    var issue = new AwdFlagIssue
                    {
                        FlagId = flagId, CompetitionId = competitionId, CompetitionChallengeId = challenge.Id,
                        TeamId = team.Id, RoundId = round.Id, RoundNumber = round.Number, RulesId = rule.Id, StoreId = store.StoreId,
                        Ordinal = ordinal, State = issueCount % 29 == 0 ? AwdFlagIssueState.Revoked : AwdFlagIssueState.Issued,
                        IssuedAt = round.StartsAt, UpdatedAt = round.StartsAt, ActiveStartTicks = round.ActiveStartTicks,
                        ExpiryRound = round.Number + 2, HistoricalExpiryRound = round.Number + 2,
                        DeliveryState = AwdFlagDeliveryState.Succeeded, Obligation = AwdFlagObligation.PlayerRequired,
                        Digest = Convert.ToHexString(digest), ProtectedReadContext = Convert.ToBase64String(protectedContext),
                        PublicLocator = $"record/{flagId:N}"
                    };
                    db.ChallengeFlags.Add(new AwdRoundChallengeFlag
                    {
                        Id = flagId, CompetitionChallengeId = challenge.Id, TeamId = team.Id, Flag = value, FlagSha256 = digest,
                        SpecificationKind = SpecificationKind.AwdRound, SpecificationId = AwdRoundSpecificationId.FromRound(round.Number).Value,
                        CreatedAt = round.StartsAt, DeletedAt = issueCount % 53 == 0 ? end : null,
                        ValidStart = round.StartsAt, ValidUntil = Wall(round.ActiveStartTicks + TimeSpan.FromMinutes(2).Ticks), AwdIssue = issue
                    });
                    history.Add(issue);
                    if (issueCount % 3 == 0)
                    {
                        var factId = Id();
                        var attacker = teams[(teamIndex + 1) % teams.Length];
                        var received = round.StartsAt.AddSeconds(20);
                        db.GameplayFacts.Add(new FlagAttemptGameplayFact
                        {
                            Id = factId, CompetitionId = competitionId, CompetitionChallengeId = challenge.Id,
                            TeamId = attacker.Id, VictimTeamId = team.Id, OccurredAt = received, UpdatedAt = received,
                            State = GameplayFactState.Completed, Result = issueCount % 37 == 0 ? GameplayFactResult.Wrong : GameplayFactResult.Correct,
                            Value = value, ValueSha256 = digest,
                            Awd = new() { FlagId = flagId, RulesId = rule.Id, ReceivedRoundId = round.Id,
                                ReceivedRoundNumber = round.Number, ReceiptScorable = issueCount % 41 != 0 }
                        });
                        db.AwdCaptureIdentities.Add(new() { CompetitionId = competitionId, FlagId = flagId,
                            GameplayFactId = factId, AttackingTeamId = attacker.Id });
                        captureCount++;
                    }
                    issueCount++;
                }
                foreach (var sample in AwdCheckPlan.Create(round, rule, round.ActiveStartTicks))
                {
                    var factId = Id();
                    var observed = Wall(sample.ActiveTicks, true);
                    var variant = checkCount % 19;
                    var flags = history.Where(issue => issue.RoundNumber >= round.Number - 1).Select(issue => new AwdFactFlagEvidence
                    {
                        FlagId = issue.FlagId, Historical = issue.RoundNumber != round.Number, RequiresWrite = issue.RoundNumber == round.Number,
                        Obligation = variant == 6 ? AwdFlagObligation.PlatformPending : AwdFlagObligation.PlayerRequired,
                        Read = variant == 7 ? AwdProbeOutcome.TargetFailure : AwdProbeOutcome.Passed,
                        Write = AwdProbeOutcome.Passed, ReadReason = variant == 7 ? AwdCheckReason.HistoricalFlagMissing : AwdCheckReason.None
                    }).ToList();
                    // Every target's plan is retained, including pending, failed, exempted and late-adjudicated evidence.
                    db.GameplayFacts.Add(new AwdServiceTransitionGameplayFact
                    {
                        Id = factId, CompetitionId = competitionId, CompetitionChallengeId = challenge.Id, TeamId = team.Id,
                        ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
                        OccurredAt = observed, UpdatedAt = observed.AddSeconds(1),
                        State = variant == 0 ? GameplayFactState.Pending : variant == 1 ? GameplayFactState.Processing
                            : variant == 2 ? GameplayFactState.PlatformFailed : GameplayFactState.Completed,
                        Result = variant <= 2 ? null : variant == 8 ? GameplayFactResult.ServiceDown : GameplayFactResult.ServiceUp,
                        Awd = new()
                        {
                            RulesId = rule.Id, ReceivedRoundId = round.Id, ReceivedRoundNumber = round.Number,
                            SampleActiveTicks = sample.ActiveTicks, SampleDeadlineActiveTicks = sample.DeadlineActiveTicks,
                            ScheduledAt = observed, Deadline = Wall(sample.DeadlineActiveTicks), ReceiptDeadline = Wall(sample.DeadlineActiveTicks).AddSeconds(30),
                            Execution = variant == 0 ? AwdCheckExecution.Pending : variant == 1 ? AwdCheckExecution.Running
                                : variant == 2 ? AwdCheckExecution.FailedToDetermine : AwdCheckExecution.Completed,
                            Settlement = variant <= 1 ? AwdSettlementState.Provisional : variant == 2 ? AwdSettlementState.PendingAdjudication
                                : variant == 3 ? AwdSettlementState.Exempted : AwdSettlementState.Final,
                            Health = variant <= 1 ? null : variant == 2 ? AwdHealth.InternalError : variant == 7 ? AwdHealth.Recovering
                                : variant == 8 ? AwdHealth.Offline : AwdHealth.Ok,
                            ObservedAt = variant <= 1 ? null : observed, ReceivedAt = variant <= 1 ? null : observed.AddSeconds(1),
                            AttemptStartedAt = variant == 0 ? null : observed,
                            AdjudicatedAt = variant == 9 ? end.AddSeconds(5) : null,
                            FinalScheduledCheck = sample.Final, ManifestComplete = variant != 10, EvidenceComplete = variant > 1,
                            RequiredFlags = flags.Count, ReadableFlags = variant == 7 ? 0 : flags.Count, Flags = flags
                        }
                    });
                    evidenceCount += flags.Count;
                    checkCount++;
                }
            }
        }
        foreach (var challenge in challenges)
        for (var revision = 1; revision <= 2; revision++)
        {
            var eventId = Id();
            var at = end.AddSeconds(revision == 1 ? -10 : 5);
            var changed = CompetitionEventGeneratedCatalog.Create(CompetitionEventKind.AwdScoringRulesChanged);
            changed.Id = eventId; changed.CompetitionId = competitionId; changed.OccurredAt = at;
            changed.CompetitionChallengeId = challenge.Entry.Id;
            db.CompetitionEvents.Add(changed);
            db.Set<AwdScoringRuleChange>().Add(new()
            {
                EventId = eventId, CompetitionId = competitionId, CompetitionChallengeId = challenge.Entry.Id,
                Sequence = revision, FirstRound = revision, LastRound = shape.Rounds - 1,
                AppliedAt = at, Timing = AwdRuleChangeTiming.RecalculateHistory,
                Policy = AwdScoringPolicy.From(rules.First(rule => rule.CompetitionChallengeId == challenge.Entry.Id))
                    with { AttackPoints = 50 + revision * 7, ServiceHealthyPoints = 100 + revision * 3 }
            });
        }
        await db.SaveChangesAsync(ct);
        return new(competitionId, start, start.AddSeconds(pauseSeconds), end,
            challenges.Select(challenge => new LeaderboardChallengeFact(challenge.Entry.Id, "Pwn", "Synthetic", false)).ToArray(),
            teams.Select((team, index) => new LeaderboardTeamFact(team.Id, team.Name, team.IsBanned, index == teams.Length - 2,
                start, EarnsScore: index != teams.Length - 3)).ToArray(),
            AwdScoringPolicy.From(rules[0]) with { AttackPoints = 91, ServiceHealthyPoints = 111 },
            new(rounds.Length, rules.Count, issueCount, captureCount, checkCount, evidenceCount));
    }

    private sealed record ProjectionCorpusShape(int Teams, int Challenges, int Rounds);
    private sealed record ProjectionCorpusCounts(int Rounds, int Rules, int FlagIssues, int CaptureIdentities, int Checks, int FlagEvidence);
    private sealed record ProjectionFixture(Guid CompetitionId, DateTimeOffset Start, DateTimeOffset PauseAt, DateTimeOffset End,
        LeaderboardChallengeFact[] Challenges, LeaderboardTeamFact[] Teams, AwdScoringPolicy PreviewPolicy, ProjectionCorpusCounts Counts);
}
