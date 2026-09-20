using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Broadcasting;

public sealed class BroadcastMessageFormatter(
    IOptions<RelayOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public static IReadOnlyList<GeneratedBroadcast> Create(
        Competition competition,
        CompetitionSnapshot previousCompetition,
        IReadOnlyDictionary<Guid, ChallengeSnapshot> previousChallenges,
        IReadOnlyList<ChallengeSnapshot>? challenges,
        IReadOnlyDictionary<Guid, TeamSnapshot> previousTeams,
        IReadOnlyList<TeamSnapshot>? teams,
        ScoreboardSnapshot? leaderboard,
        IReadOnlyList<CompetitionSignal> signals)
    {
        var messages = new List<GeneratedBroadcast>();
        var lifecycle = LifecycleText(previousCompetition.Status, competition.Status);
        if (lifecycle is not null)
        {
            messages.Add(new(
                $"lifecycle:{competition.Status}:{competition.StartTime.ToUnixTimeSeconds()}:{competition.EndTime.ToUnixTimeSeconds()}",
                BroadcastCategory.General,
                $"▌NoCTF · {Plain(competition.Title, 120)}\n▷ {lifecycle}"));
        }

        var challengeById = challenges?.ToDictionary(item => item.ChallengeId) ?? [];
        foreach (var signal in signals.OrderBy(item => item.OccurredAt))
        {
            var key = signal.EventId?.ToString("N")
                ?? $"{signal.Kind}:{signal.OccurredAt.ToUnixTimeMilliseconds()}";
            switch (signal.Kind)
            {
                case "ChallengePublished":
                    {
                        var published = challengeById.Values
                            .Where(item => !previousChallenges.ContainsKey(item.ChallengeId))
                            .OrderBy(item => item.Title)
                            .Select(item => item.Title)
                            .ToArray();
                        var suffix = published.Length == 0
                            ? "平台开放了新题目，请前往比赛页面查看。"
                            : $"新题开放：{string.Join("、", published.Select(item => Plain(item, 100)))}";
                        messages.Add(new(key, BroadcastCategory.General, $"▌NoCTF\n▷ {suffix}"));
                        break;
                    }
                case "ChallengeDescriptionUpdated":
                    {
                        var updated = challengeById.Values
                            .Where(item => previousChallenges.TryGetValue(item.ChallengeId, out var previous)
                                && previous.ContentHash != item.ContentHash)
                            .Select(item => item.Title)
                            .ToArray();
                        var subject = updated.Length == 0
                            ? "某道题"
                            : string.Join("、", updated.Select(item => Plain(item, 100)));
                        var separator = updated.Length == 0 ? string.Empty : " ";
                        messages.Add(new(
                            key,
                            BroadcastCategory.General,
                            $"▌NoCTF\n▷ {subject}{separator}的题目内容已更新，请前往平台查看。"));
                        break;
                    }
                case "HintPublished":
                    messages.Add(new(
                        key,
                        BroadcastCategory.General,
                        "▌NoCTF\n▷ 平台发布了新提示；BOT 不会自动解锁或转发提示正文，请前往比赛页面查看。"));
                    break;
                case "AnnouncementPublished":
                    messages.Add(new(
                        key,
                        BroadcastCategory.General,
                        "▌NoCTF\n▷ 平台发布了新公告，请前往比赛页面查看。"));
                    break;
                case "TeamBanned":
                    messages.Add(new(
                        key,
                        BroadcastCategory.General,
                        "▌NoCTF\n▷ 平台公布了一项队伍封禁事件，请以比赛页面公开信息为准。"));
                    break;
                case "TeamBanCorrectionPublished":
                    messages.Add(new(
                        key,
                        BroadcastCategory.General,
                        "▌NoCTF\n▷ 平台公布了一项队伍封禁纠正事件，请以比赛页面公开信息为准。"));
                    break;
            }
        }

        if (teams is not null && leaderboard is not null)
        {
            if (leaderboard.DataScope != LeaderboardDataScope.Hidden
                && leaderboard.Visibility != LeaderboardVisibility.Blackout)
            {
                messages.AddRange(BloodMessages(previousTeams, teams, challengeById, signals));
            }
            var hasAwdpResolution = signals.Any(signal => signal.Kind is
                "AwdpBreakResolved" or "AwdpFixResolved");
            var scoreMessage = hasAwdpResolution
                ? ScoreboardMessage(previousTeams, teams, leaderboard)
                : null;
            if (scoreMessage is not null) messages.Add(scoreMessage);
        }
        return [.. messages
            .GroupBy(item => (item.DedupeKey, item.Category))
            .Select(group => group.First())];
    }

    public string Status(Competition competition)
    {
        var now = timeProvider.GetUtcNow();
        var remaining = competition.EndTime - now;
        var timing = competition.Status switch
        {
            CompetitionStatus.Draft or CompetitionStatus.Visible or CompetitionStatus.Published
                when competition.StartTime > now => $"▷ Remain：{Duration(competition.StartTime - now)}",
            CompetitionStatus.Running or CompetitionStatus.Paused when remaining > TimeSpan.Zero =>
                $"▷ Remain：{Duration(remaining)}",
            _ => ""
        };
        return $"""
            ▌NoCTF · {Plain(competition.Title, 120)}
            ▷ 状态：{StatusText(competition.Status)}
            ▷ Start：{LocalTime(competition.StartTime)}
            ▷ End：{LocalTime(competition.EndTime)}
            {timing}
            """.TrimEnd();
    }

    public static string Challenges(ChallengeList challenges)
    {
        if (challenges.Items.Count == 0) return "▌NoCTF\n▷ 当前没有可见题目。";
        var lines = challenges.Items
            .OrderBy(item => item.Order)
            .ThenBy(item => item.DisplayTitle)
            .Select(item => $"▷ [{Plain(item.Direction, 32)}] {Plain(item.DisplayTitle, 100)}")
            .ToArray();
        var message = new StringBuilder("▌NoCTF · 当前可见题目");
        var included = 0;
        foreach (var line in lines)
        {
            if (message.Length + line.Length + 1 > 3500) break;
            message.Append('\n').Append(line);
            included++;
        }
        if (included < lines.Length)
            message.Append("\n…另有 ").Append(lines.Length - included).Append(" 道题，请前往平台查看。");
        return message.ToString();
    }

    public static IReadOnlyList<string> Rank(
        ScoreboardSnapshot scoreboard,
        int limit,
        string? trackKey = null)
    {
        if (scoreboard.DataScope == LeaderboardDataScope.Hidden
            || scoreboard.Visibility == LeaderboardVisibility.Blackout)
        {
            return ["▌NoCTF\n▷ 排行榜当前不可见。BOT 不会推测实时分数或名次。"];
        }
        var eligible = scoreboard.Teams
            .Where(team => team.RankingState == ScoreboardRankingState.Eligible)
            .ToArray();
        var rankedTeams = string.IsNullOrWhiteSpace(trackKey)
            ? eligible
                .OrderByDescending(team => team.TotalScore)
                .ThenBy(team => team.Rank ?? int.MaxValue)
                .ThenBy(team => team.TeamName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(team => team.TeamId)
                .Select((team, index) => new RankedTeam(team, index + 1))
            : eligible
                .Where(team => string.Equals(
                    team.TrackKey,
                    trackKey,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(team => team.Rank ?? int.MaxValue)
                .ThenByDescending(team => team.TotalScore)
                .ThenBy(team => team.TeamName, StringComparer.OrdinalIgnoreCase)
                .Select(team => new RankedTeam(team, team.Rank));
        var teams = rankedTeams
            .Take(limit)
            .ToArray();
        if (teams.Length == 0) return ["▌NoCTF\n▷ 排行榜暂时没有可显示的队伍。"];
        var selectedTrack = string.IsNullOrWhiteSpace(trackKey)
            ? null
            : teams[0].Team.TrackKey;
        var header = scoreboard.DataScope == LeaderboardDataScope.Frozen
            ? selectedTrack is null
                ? $"▌NoCTF · 冻结排行榜（截至 {scoreboard.DataAsOf:yyyy-MM-dd HH:mm:ss zzz}）"
                : $"▌NoCTF · {Plain(selectedTrack, 32)} 赛道冻结排行榜（截至 {scoreboard.DataAsOf:yyyy-MM-dd HH:mm:ss zzz}）"
            : selectedTrack is null
                ? "▌NoCTF · 实时排行榜"
                : $"▌NoCTF · {Plain(selectedTrack, 32)} 赛道实时排行榜";
        var rows = teams.Select(item =>
            $"▷ #{item.Rank?.ToString(CultureInfo.InvariantCulture) ?? "-"} {Plain(item.Team.TeamName, 100)}  {item.Team.TotalScore} pts");
        var all = new[] { header }.Concat(rows).ToArray();
        if (all.Length <= 12) return [string.Join('\n', all)];
        return [string.Join('\n', all[..12]), string.Join('\n', all[12..])];
    }

    public static string Team(ScoreboardSnapshot scoreboard, string teamName)
    {
        if (scoreboard.DataScope == LeaderboardDataScope.Hidden
            || scoreboard.Visibility == LeaderboardVisibility.Blackout)
        {
            return "▌NoCTF\n▷ 排行榜当前不可见。";
        }
        var matches = scoreboard.Teams
            .Where(team => team.TeamName.Contains(teamName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(team => team.Rank ?? int.MaxValue)
            .Take(5)
            .ToArray();
        return matches.Length switch
        {
            0 => $"▌NoCTF\n▷ 公开排行榜中未找到队伍「{Plain(teamName, 100)}」。",
            1 => $"▌NoCTF · {Plain(matches[0].TeamName, 100)}\n▷ Rank：#{matches[0].Rank?.ToString(CultureInfo.InvariantCulture) ?? "-"}\n▷ Score：{matches[0].TotalScore} pts",
            _ => "▌NoCTF · 多支匹配队伍\n" + string.Join('\n', matches.Select(item =>
                $"▷ #{item.Rank?.ToString(CultureInfo.InvariantCulture) ?? "-"} {Plain(item.TeamName, 100)}  {item.TotalScore} pts"))
        };
    }

    private static IEnumerable<GeneratedBroadcast> BloodMessages(
        IReadOnlyDictionary<Guid, TeamSnapshot> previousTeams,
        IReadOnlyList<TeamSnapshot> teams,
        IReadOnlyDictionary<Guid, ChallengeSnapshot> challenges,
        IReadOnlyList<CompetitionSignal> signals)
    {
        var bloodSignals = signals
            .Where(signal => signal.Kind is "FirstBloodAwarded" or "SecondBloodAwarded" or "ThirdBloodAwarded")
            .OrderBy(signal => signal.OccurredAt)
            .ToArray();
        if (bloodSignals.Length == 0) return [];
        var newAchievements = new List<(TeamSnapshot Team, ScoreboardAchievement Achievement)>();
        foreach (var team in teams)
        {
            var previous = previousTeams.TryGetValue(team.TeamId, out var found)
                ? DeserializeAchievements(found.AchievementsJson)
                : [];
            var seen = previous.Select(AchievementKey).ToHashSet(StringComparer.Ordinal);
            newAchievements.AddRange(
                DeserializeAchievements(team.AchievementsJson)
                    .Where(item => item.Kind == ScoreboardEntryKind.Solve
                        && !seen.Contains(AchievementKey(item)))
                    .Select(item => (team, item)));
        }
        var remaining = newAchievements.OrderBy(item => item.Achievement.OccurredAt).ToList();
        var messages = new List<GeneratedBroadcast>();
        foreach (var signal in bloodSignals)
        {
            var key = signal.EventId?.ToString("N")
                ?? $"{signal.Kind}:{signal.OccurredAt.ToUnixTimeMilliseconds()}";
            var rank = signal.Kind switch
            {
                "FirstBloodAwarded" => "一血",
                "SecondBloodAwarded" => "二血",
                _ => "三血"
            };
            var matchIndex = ClosestAchievementIndex(remaining, signal.OccurredAt);
            if (matchIndex < 0)
            {
                messages.Add(new(key, BroadcastCategory.Blood, $"▌NoCTF\n▷ 产生{rank}，请前往排行榜查看。"));
                continue;
            }
            var achievement = remaining[matchIndex];
            remaining.RemoveAt(matchIndex);
            var challenge = challenges.TryGetValue(
                achievement.Achievement.CompetitionChallengeId,
                out var challengeSnapshot)
                ? challengeSnapshot.Title
                : "未知题目";
            messages.Add(new(
                key,
                BroadcastCategory.Blood,
                $"▌NoCTF\n▷ {Plain(achievement.Team.TeamName, 100)} 获得题目「{Plain(challenge, 100)}」{rank}！"));
        }
        return messages;
    }

    private static int ClosestAchievementIndex(
        IReadOnlyList<(TeamSnapshot Team, ScoreboardAchievement Achievement)> achievements,
        DateTimeOffset occurredAt)
    {
        var bestIndex = -1;
        var bestDistance = TimeSpan.MaxValue;
        for (var index = 0; index < achievements.Count; index++)
        {
            var distance = (achievements[index].Achievement.OccurredAt - occurredAt).Duration();
            if (distance > TimeSpan.FromSeconds(30) || distance >= bestDistance) continue;
            bestIndex = index;
            bestDistance = distance;
        }
        return bestIndex;
    }

    private static GeneratedBroadcast? ScoreboardMessage(
        IReadOnlyDictionary<Guid, TeamSnapshot> previousTeams,
        IReadOnlyList<TeamSnapshot> teams,
        ScoreboardSnapshot leaderboard)
    {
        if (leaderboard.DataScope == LeaderboardDataScope.Hidden
            || leaderboard.Visibility == LeaderboardVisibility.Blackout)
        {
            return null;
        }
        var changed = teams
            .Where(team => team.Rank is not null
                && (!previousTeams.TryGetValue(team.TeamId, out var previous)
                    || previous.Rank != team.Rank
                    || previous.Score != team.Score))
            .OrderBy(team => team.Rank)
            .Take(8)
            .ToArray();
        if (changed.Length == 0) return null;
        var lines = changed.Select(team =>
        {
            previousTeams.TryGetValue(team.TeamId, out var previous);
            var delta = previous is null ? team.Score : team.Score - previous.Score;
            var deltaText = delta == 0 ? string.Empty : $" ({delta:+#;-#;0})";
            return $"▷ #{team.Rank} {Plain(team.TeamName, 100)}  {team.Score} pts{deltaText}";
        });
        var scope = leaderboard.DataScope == LeaderboardDataScope.Frozen ? "冻结榜" : "排行榜";
        return new(
            $"scoreboard:{leaderboard.Version}:{leaderboard.DataScope}",
            BroadcastCategory.Scoreboard,
            $"▌NoCTF · {scope}更新\n{string.Join('\n', lines)}");
    }

    private string LocalTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, timeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d {value.Hours}h"
            : value.TotalHours >= 1
                ? $"{(int)value.TotalHours}h {value.Minutes}m"
                : $"{Math.Max(0, value.Minutes)}m";
    }

    private static string StatusText(CompetitionStatus status) => status switch
    {
        CompetitionStatus.Draft => "草稿",
        CompetitionStatus.Visible => "可见",
        CompetitionStatus.Published => "已发布",
        CompetitionStatus.Running => "进行中",
        CompetitionStatus.Paused => "已暂停",
        CompetitionStatus.Finished => "已结束",
        _ => status.ToString()
    };

    private static string? LifecycleText(
        CompetitionStatus previous,
        CompetitionStatus current) => current switch
        {
            CompetitionStatus.Running when previous == CompetitionStatus.Paused => "比赛已恢复。",
            CompetitionStatus.Running => "比赛已开始。",
            CompetitionStatus.Paused => "比赛已暂停。",
            CompetitionStatus.Finished => "比赛已结束。",
            _ => null
        };

    private static ScoreboardAchievement[] DeserializeAchievements(string json) =>
        JsonSerializer.Deserialize<ScoreboardAchievement[]>(json, JsonOptions) ?? [];

    private static string AchievementKey(ScoreboardAchievement item) =>
        $"{item.CompetitionChallengeId:N}|{item.Kind}|{item.UserId:N}|{item.OccurredAt:O}";

    private static string Plain(string value, int maximumLength)
    {
        var normalized = new string([.. value.Where(character => !char.IsControl(character))])
            .Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private sealed record RankedTeam(ScoreboardTeam Team, int? Rank);
}
