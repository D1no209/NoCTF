using System.Globalization;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.Milky;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Commands;

public sealed class BotCommandProcessor(
    BotStateStore store,
    NoCtfClient noCtf,
    MilkyClient milky,
    BroadcastMessageFormatter formatter,
    ICompetitionRefreshScheduler refresh,
    ICompetitionSubscriptionMonitor subscriptions,
    BotRuntimeState runtimeState,
    IOptions<NoCtfBotOptions> noCtfOptions,
    IOptions<RelayOptions> relayOptions,
    SlidingWindowLimiter limiter,
    ILogger<BotCommandProcessor> logger)
{
    private readonly NoCtfBotOptions noCtfOptions = noCtfOptions.Value;
    private readonly HashSet<long> allowedGroups = [.. relayOptions.Value.AllowedGroupIds];

    public async Task ProcessAsync(MilkyGroupMessage message, CancellationToken ct)
    {
        if (message.Text.Length > 512) return;
        var command = BotCommandParser.Parse(message.Text);
        if (command is null) return;
        if (allowedGroups.Count > 0 && !allowedGroups.Contains(message.GroupId)) return;
        if (!store.TryRecordInbound("group", message.GroupId, message.MessageSequence)) return;
        if (!limiter.TryAcquire($"user:{message.SenderId}", 5, TimeSpan.FromSeconds(10))
            || !limiter.TryAcquire($"group:{message.GroupId}", 30, TimeSpan.FromMinutes(1)))
        {
            Reply(message, 0, "【NoCTF】请求过于频繁，请稍后再试。");
            return;
        }

        try
        {
            var replies = await ExecuteAsync(message, command, ct);
            for (var index = 0; index < replies.Count; index++)
                Reply(message, index, replies[index]);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "BOT command {CommandKind} failed in group {GroupId} with {ErrorType}.",
                command.Kind,
                message.GroupId,
                exception.GetType().Name);
            Reply(message, 0, "【NoCTF】命令暂时无法处理，请稍后再试。");
        }
    }

    private async Task<IReadOnlyList<string>> ExecuteAsync(
        MilkyGroupMessage message,
        BotCommand command,
        CancellationToken ct)
    {
        if (command.Kind == BotCommandKind.Help) return [HelpText];
        if (command.Kind == BotCommandKind.Invalid) return ["【NoCTF】命令格式无效。\n" + HelpText];
        if (command.Kind is BotCommandKind.Subscribe
            or BotCommandKind.Unsubscribe
            or BotCommandKind.Broadcasts
            or BotCommandKind.Scoreboard
            or BotCommandKind.Blood
            or BotCommandKind.Config)
        {
            return [await ExecuteAdministrativeAsync(message, command, ct)];
        }

        var subscription = store.GetSubscription(message.GroupId);
        if (subscription is null)
            return ["【NoCTF】本群尚未订阅比赛，请由群主或群管理员执行 /ctf subscribe <competitionId>。"];
        if (subscription.SuspendedReason is not null)
            return ["【NoCTF】本群订阅已暂停，请由群主或群管理员重新执行 subscribe。"];
        if (!runtimeState.IsNoCtfAuthorized)
            return ["【NoCTF】BOT 凭据已失效，查询和播报已暂停。"];

        return command.Kind switch
        {
            BotCommandKind.Status => [await StatusAsync(subscription.CompetitionId, ct)],
            BotCommandKind.Challenges => [await ChallengesAsync(subscription.CompetitionId, ct)],
            BotCommandKind.Rank => await RankAsync(subscription.CompetitionId, command.RankLimit, ct),
            BotCommandKind.Team => [await TeamAsync(subscription.CompetitionId, command.Argument!, ct)],
            BotCommandKind.Link => [CompetitionLink(subscription.CompetitionId)],
            _ => ["【NoCTF】命令格式无效。"]
        };
    }

    private async Task<string> ExecuteAdministrativeAsync(
        MilkyGroupMessage message,
        BotCommand command,
        CancellationToken ct)
    {
        var member = await milky.GetGroupMemberAsync(message.GroupId, message.SenderId, ct);
        if (member.Role is not (MilkyGroupRole.Admin or MilkyGroupRole.Owner))
            return "【NoCTF】只有群主或群管理员可以修改订阅配置。";

        if (command.Kind == BotCommandKind.Subscribe)
        {
            if (!runtimeState.IsNoCtfAuthorized)
                return "【NoCTF】BOT 凭据已失效，无法订阅比赛。";
            var competitionId = Guid.Parse(command.Argument!);
            var result = await noCtf.GetCompetitionAsync(competitionId, ct);
            if (result.State == NoCtfReadState.Unauthorized)
            {
                return Unauthorized();
            }
            if (result.State != NoCtfReadState.Available
                || result.Value is null
                || result.Value.AccessMode != CompetitionAccessMode.Public
                || result.Value.Status == CompetitionStatus.Draft)
            {
                return "【NoCTF】该比赛当前不可公开访问，未创建订阅。";
            }
            store.CancelPendingOutbound(message.GroupId);
            store.UpsertSubscription(message.GroupId, competitionId);
            refresh.Schedule(competitionId, CompetitionRefreshReason.Initial, delay: TimeSpan.Zero);
            subscriptions.SignalChanged();
            return $"【NoCTF】已订阅比赛“{result.Value.Title}”。\n{CompetitionLink(competitionId)}";
        }

        if (command.Kind == BotCommandKind.Unsubscribe)
        {
            store.CancelPendingOutbound(message.GroupId);
            var removed = store.DeleteSubscription(message.GroupId);
            subscriptions.SignalChanged();
            return removed ? "【NoCTF】已取消本群订阅。" : "【NoCTF】本群当前没有订阅。";
        }

        var subscription = store.GetSubscription(message.GroupId);
        if (subscription is null)
            return "【NoCTF】本群尚未订阅比赛。";
        if (command.Kind == BotCommandKind.Config) return Configuration(subscription);

        var column = command.Kind switch
        {
            BotCommandKind.Broadcasts => "broadcast_enabled",
            BotCommandKind.Scoreboard => "scoreboard_enabled",
            BotCommandKind.Blood => "blood_enabled",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        store.SetSubscriptionOption(message.GroupId, column, command.Enabled!.Value);
        return $"【NoCTF】{OptionName(command.Kind)}已{(command.Enabled.Value ? "开启" : "关闭")}。";
    }

    private async Task<string> StatusAsync(Guid competitionId, CancellationToken ct)
    {
        var result = await noCtf.GetCompetitionAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => formatter.Status(result.Value!),
            NoCtfReadState.NotFound => "【NoCTF】比赛当前不可访问。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "【NoCTF】平台暂时不可用，请稍后再试。"
        };
    }

    private async Task<string> ChallengesAsync(Guid competitionId, CancellationToken ct)
    {
        var result = await noCtf.GetChallengesAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Challenges(result.Value!),
            NoCtfReadState.NotFound => "【NoCTF】比赛或题目列表当前不可访问。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "【NoCTF】题目列表暂时不可用，请稍后再试。"
        };
    }

    private async Task<IReadOnlyList<string>> RankAsync(
        Guid competitionId,
        int limit,
        CancellationToken ct)
    {
        var result = await noCtf.GetLeaderboardAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Rank(result.Value!, limit),
            NoCtfReadState.Processing =>
                [$"【NoCTF】排行榜正在生成，请在 {Math.Ceiling((result.RetryAfter ?? TimeSpan.FromSeconds(2)).TotalSeconds).ToString(CultureInfo.InvariantCulture)} 秒后重试。"],
            NoCtfReadState.NotFound => ["【NoCTF】比赛当前不可访问。"],
            NoCtfReadState.Unauthorized => [Unauthorized()],
            _ => ["【NoCTF】排行榜投影暂时不可用，请稍后再试。"]
        };
    }

    private async Task<string> TeamAsync(
        Guid competitionId,
        string teamName,
        CancellationToken ct)
    {
        var result = await noCtf.GetLeaderboardAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Team(result.Value!, teamName),
            NoCtfReadState.Processing => "【NoCTF】排行榜正在生成，请稍后再试。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "【NoCTF】暂时无法查询该队伍。"
        };
    }

    private string Unauthorized()
    {
        if (runtimeState.MarkNoCtfUnauthorized())
        {
            foreach (var subscription in store.GetSubscriptions())
            {
                store.EnqueueOutbound(
                    $"token-invalid:{subscription.GroupId}",
                    subscription.GroupId,
                    "【NoCTF】BOT 凭据已失效，自动播报和查询已暂停，请联系管理员轮换 Token。");
            }
        }
        return "【NoCTF】BOT 凭据已失效，查询和播报已暂停。";
    }

    private void Reply(MilkyGroupMessage message, int part, string payload)
    {
        store.EnqueueOutbound(
            $"command:{message.GroupId}:{message.MessageSequence}:{part}",
            message.GroupId,
            payload);
    }

    private string CompetitionLink(Guid competitionId) =>
        $"【NoCTF】{new Uri(noCtfOptions.PublicBaseUrl, $"competitions/{competitionId:D}")}";

    private static string Configuration(GroupSubscription subscription) => $"""
        【NoCTF】本群订阅配置
        比赛：{subscription.CompetitionId:D}
        通知：{OnOff(subscription.BroadcastEnabled)}
        排行榜：{OnOff(subscription.ScoreboardEnabled)}
        血榜：{OnOff(subscription.BloodEnabled)}
        状态：{(subscription.SuspendedReason is null ? "正常" : "已暂停")}
        """;

    private static string OnOff(bool enabled) => enabled ? "开启" : "关闭";

    private static string OptionName(BotCommandKind kind) => kind switch
    {
        BotCommandKind.Broadcasts => "赛事通知",
        BotCommandKind.Scoreboard => "排行榜播报",
        BotCommandKind.Blood => "血榜播报",
        _ => "配置"
    };

    private const string HelpText = """
        【NoCTF】可用命令
        /ctf status
        /ctf challenges
        /ctf rank [1-20]
        /ctf team <队伍名>
        /ctf link
        群管理员：subscribe、unsubscribe、broadcasts on|off、scoreboard on|off、blood on|off、config
        """;
}
