using NoCTF.Core;

namespace NoCTF.Plugins.QQBot;

internal static class QqBotDefaults
{
    public static readonly Guid GlobalSettingsId = Guid.Parse("00000000-0000-0000-0000-00000000b071");

    public static string Template(QqBotEventType eventType) => eventType switch
    {
        QqBotEventType.CompetitionStarted => "🏁 {competition_name}\n比赛已开始\n{occurred_at}\n{competition_url}",
        QqBotEventType.ChallengePublished => "🆕 新题发布｜{problem_title}\n分类：{problem_category}\n{problem_url}",
        QqBotEventType.HintPublished => "💡 新提示｜{problem_title}\n{hint_content}\n{problem_url}",
        QqBotEventType.FirstBlood => "🥇 一血｜{problem_title}\n{team_name}\n{occurred_at}",
        QqBotEventType.SecondBlood => "🥈 二血｜{problem_title}\n{team_name}\n{occurred_at}",
        QqBotEventType.ThirdBlood => "🥉 三血｜{problem_title}\n{team_name}\n{occurred_at}",
        QqBotEventType.TeamPenalized => "⚠️ 处罚通知｜{competition_name}\n对象：{team_name}\n处罚：{penalty_type}\n原因：{penalty_reason}\n{occurred_at}",
        QqBotEventType.Announcement => "📢 {announcement_title}\n{announcement_content}\n{occurred_at}",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null)
    };

    public static IReadOnlyList<string> AllowedVariables(QqBotEventType eventType) => eventType switch
    {
        QqBotEventType.CompetitionStarted =>
            ["competition_name", "occurred_at", "competition_url"],
        QqBotEventType.ChallengePublished =>
            ["competition_name", "problem_title", "problem_category", "occurred_at", "competition_url", "problem_url"],
        QqBotEventType.HintPublished =>
            ["competition_name", "problem_title", "problem_category", "hint_title", "hint_content", "occurred_at", "competition_url", "problem_url"],
        QqBotEventType.FirstBlood or QqBotEventType.SecondBlood or QqBotEventType.ThirdBlood =>
            ["competition_name", "problem_title", "problem_category", "team_name", "user_name", "blood_rank", "occurred_at", "competition_url", "problem_url"],
        QqBotEventType.TeamPenalized =>
            ["competition_name", "team_name", "penalty_type", "penalty_reason", "occurred_at", "competition_url"],
        QqBotEventType.Announcement =>
            ["competition_name", "announcement_title", "announcement_content", "occurred_at", "competition_url"],
        _ => []
    };
}
