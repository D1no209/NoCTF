using System.Globalization;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Commands;

public sealed class BotCommandProcessor(
    BotStateStore store,
    NoCtfClient noCtf,
    BroadcastMessageFormatter formatter,
    ICompetitionRefreshScheduler refresh,
    ICompetitionSubscriptionMonitor subscriptions,
    BotRuntimeState runtimeState,
    ChatProviderCatalog providers,
    OutboundMessageQueue outbound,
    IOptions<NoCtfBotOptions> noCtfOptions,
    IOptions<RelayOptions> relayOptions,
    TimeProvider timeProvider,
    SlidingWindowLimiter limiter,
    ILogger<BotCommandProcessor> logger)
{
    private static readonly Guid MaximumGuid =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private readonly NoCtfBotOptions noCtfOptions = noCtfOptions.Value;
    private readonly HashSet<string> allowedGroups =
        new(relayOptions.Value.AllowedGroupIds, StringComparer.Ordinal);

    public async Task ProcessAsync(ChatGroupMessage message, CancellationToken ct)
    {
        if (message.Text.Length > 512
            || !string.Equals(message.ProviderId, providers.Active.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var command = BotCommandParser.Parse(message.Text);
        if (command is null) return;
        if (allowedGroups.Count > 0 && !allowedGroups.Contains(message.GroupId)) return;
        if (!store.TryRecordInbound(message.ProviderId, message.GroupId, message.MessageId)) return;
        if (!limiter.TryAcquire($"user:{message.ProviderId}:{message.SenderId}", 5, TimeSpan.FromSeconds(10))
            || !limiter.TryAcquire($"group:{message.ProviderId}:{message.GroupId}", 30, TimeSpan.FromMinutes(1)))
        {
            Reply(message, 0, "▌NoCTF\n▷ 请求过于频繁，请稍后再试。");
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
                "BOT command {CommandKind} failed through {ProviderId} in group {GroupId} with {ErrorType}.",
                command.Kind,
                message.ProviderId,
                message.GroupId,
                exception.GetType().Name);
            Reply(message, 0, "▌NoCTF\n▷ 命令暂时无法处理，请稍后再试。");
        }
    }

    private async Task<IReadOnlyList<string>> ExecuteAsync(
        ChatGroupMessage message,
        BotCommand command,
        CancellationToken ct)
    {
        var isMaster = message.SenderId == providers.MasterUserId;
        var access = store.GetGroupAccess(message.ProviderId, message.GroupId);
        if (access?.MasterAuthorizedAt is null)
        {
            if (command.Kind == BotCommandKind.Enable && isMaster)
            {
                store.AuthorizeAndEnableGroup(message.ProviderId, message.GroupId);
                var existing = store.GetSubscription(message.ProviderId, message.GroupId);
                if (existing is not null)
                {
                    await ResetAnnouncementCheckpointAsync(existing, ct);
                    refresh.Schedule(
                        existing.CompetitionId,
                        CompetitionRefreshReason.Initial,
                        delay: TimeSpan.Zero);
                }
                subscriptions.SignalChanged();
                return existing is null
                    ? ["▌NoCTF\n▷ master 已授权并启用本群 BOT，请由管理员执行 bind <competitionId>。"]
                    : [$"▌NoCTF\n▷ master 已授权并启用本群 BOT，已恢复比赛绑定：{existing.CompetitionId:D}。"];
            }
            if (command.Kind == BotCommandKind.Revoke && isMaster)
                return ["▌NoCTF\n▷ 本群尚未获得 master 授权。"];
            return [];
        }

        if (!access.Enabled)
        {
            if (command.Kind == BotCommandKind.Revoke && isMaster)
            {
                store.RevokeGroup(message.ProviderId, message.GroupId);
                subscriptions.SignalChanged();
                return ["▌NoCTF\n▷ 已撤销本群 BOT 授权。"];
            }
            if (command.Kind != BotCommandKind.Enable
                || !await IsAdministratorAsync(message, isMaster, ct))
            {
                return [];
            }
            store.SetGroupEnabled(message.ProviderId, message.GroupId, true);
            var existing = store.GetSubscription(message.ProviderId, message.GroupId);
            if (existing is not null)
            {
                await ResetAnnouncementCheckpointAsync(existing, ct);
                refresh.Schedule(existing.CompetitionId, CompetitionRefreshReason.Initial, delay: TimeSpan.Zero);
            }
            subscriptions.SignalChanged();
            return ["▌NoCTF\n▷ 已重新启用本群 BOT。"];
        }

        if (command.Kind == BotCommandKind.Enable)
            return ["▌NoCTF\n▷ 本群 BOT 已处于启用状态。"];
        if (command.Kind == BotCommandKind.Revoke)
        {
            if (!isMaster) return ["▌NoCTF\n▷ 只有 master 可以撤销群授权。"];
            store.RevokeGroup(message.ProviderId, message.GroupId);
            subscriptions.SignalChanged();
            return ["▌NoCTF\n▷ 已撤销本群 BOT 授权并清除群配置。"];
        }
        if (command.Kind is BotCommandKind.AdminAdd
            or BotCommandKind.AdminRemove
            or BotCommandKind.AdminList)
        {
            return [ExecuteMasterAdministration(message, command, isMaster)];
        }
        if (command.Kind == BotCommandKind.Disable)
        {
            if (!await IsAdministratorAsync(message, isMaster, ct))
                return ["▌NoCTF\n▷ 只有 master 或本群管理员可以关闭 BOT。"];
            store.CancelPendingOutbound(message.ProviderId, message.GroupId, "group_disabled");
            store.SetGroupEnabled(message.ProviderId, message.GroupId, false);
            subscriptions.SignalChanged();
            return ["▌NoCTF\n▷ 已关闭本群 BOT；首次授权仍保留，管理员可以重新 enable。"];
        }

        if (command.Kind == BotCommandKind.Help) return [HelpText];
        if (command.Kind == BotCommandKind.Invalid) return ["▌NoCTF\n▷ 命令格式无效。\n" + HelpText];
        if (command.Kind is BotCommandKind.Bind
            or BotCommandKind.Unbind
            or BotCommandKind.Broadcasts
            or BotCommandKind.Scoreboard
            or BotCommandKind.Blood
            or BotCommandKind.Config)
        {
            if (!await IsAdministratorAsync(message, isMaster, ct))
                return ["▌NoCTF\n▷ 只有 master、群主、群管理员或本群自定义 admin 可以修改配置。"];
            return [await ExecuteAdministrativeAsync(message, command, ct)];
        }

        var subscription = store.GetSubscription(message.ProviderId, message.GroupId);
        if (subscription is null)
            return ["▌NoCTF\n▷ 本群尚未绑定比赛，请由管理员执行 bind <competitionId>。"];
        if (subscription.SuspendedReason is not null)
            return ["▌NoCTF\n▷ 本群绑定已暂停，请由管理员重新执行 bind。"];
        if (!runtimeState.IsNoCtfAuthorized)
            return ["▌NoCTF\n▷ 平台凭据已失效，查询和播报已暂停。"];

        return command.Kind switch
        {
            BotCommandKind.Status => [await StatusAsync(subscription.CompetitionId, ct)],
            BotCommandKind.Challenges => [await ChallengesAsync(subscription.CompetitionId, ct)],
            BotCommandKind.Rank => await RankAsync(
                subscription.CompetitionId,
                command.Argument,
                command.RankLimit,
                ct),
            BotCommandKind.Team => [await TeamAsync(subscription.CompetitionId, command.Argument!, ct)],
            BotCommandKind.Link => [CompetitionLink(subscription.CompetitionId)],
            _ => ["▌NoCTF\n▷ 命令格式无效。"]
        };
    }

    private async Task<bool> IsAdministratorAsync(
        ChatGroupMessage message,
        bool isMaster,
        CancellationToken ct)
    {
        if (isMaster
            || store.IsGroupAdmin(message.ProviderId, message.GroupId, message.SenderId))
        {
            return true;
        }
        var role = await providers.Active.GetGroupMemberRoleAsync(
            message.GroupId,
            message.SenderId,
            ct);
        return role is ChatGroupRole.Admin or ChatGroupRole.Owner;
    }

    private string ExecuteMasterAdministration(
        ChatGroupMessage message,
        BotCommand command,
        bool isMaster)
    {
        if (!isMaster) return "▌NoCTF\n▷ 只有 master 可以管理自定义 admin。";
        if (command.Kind == BotCommandKind.AdminList)
        {
            var admins = store.GetGroupAdmins(message.ProviderId, message.GroupId);
            return admins.Count == 0
                ? "▌NoCTF\n▷ 本群没有自定义 admin。"
                : "▌NoCTF · 本群自定义 admin\n" + string.Join('\n', admins.Select(admin => $"▷ {admin}"));
        }
        if (!providers.Active.TryNormalizeUserId(command.Argument!, out var userId))
            return "▌NoCTF\n▷ 用户 ID 格式无效。";
        if (command.Kind == BotCommandKind.AdminAdd)
        {
            store.AddGroupAdmin(message.ProviderId, message.GroupId, userId, message.SenderId);
            return $"▌NoCTF\n▷ 已将 {userId} 添加为本群自定义 admin。";
        }
        return store.RemoveGroupAdmin(message.ProviderId, message.GroupId, userId)
            ? $"▌NoCTF\n▷ 已移除本群自定义 admin：{userId}。"
            : "▌NoCTF\n▷ 该用户不是本群自定义 admin。";
    }

    private async Task<string> ExecuteAdministrativeAsync(
        ChatGroupMessage message,
        BotCommand command,
        CancellationToken ct)
    {
        if (command.Kind == BotCommandKind.Bind)
        {
            if (!runtimeState.IsNoCtfAuthorized)
                return "▌NoCTF\n▷ 平台凭据已失效，无法绑定比赛。";
            var competitionId = Guid.Parse(command.Argument!);
            var result = await noCtf.GetCompetitionAsync(competitionId, ct);
            if (result.State == NoCtfReadState.Unauthorized) return Unauthorized();
            if (result.State != NoCtfReadState.Available
                || result.Value is null
                || result.Value.AccessMode != CompetitionAccessMode.Public
                || result.Value.Status == CompetitionStatus.Draft)
            {
                return "▌NoCTF\n▷ 该比赛当前不可公开访问，未创建绑定。";
            }
            store.CancelPendingOutbound(message.ProviderId, message.GroupId);
            store.UpsertSubscription(message.ProviderId, message.GroupId, competitionId);
            await ResetAnnouncementCheckpointAsync(
                store.GetSubscription(message.ProviderId, message.GroupId)!,
                ct);
            refresh.Schedule(competitionId, CompetitionRefreshReason.Initial, delay: TimeSpan.Zero);
            subscriptions.SignalChanged();
            return $"▌NoCTF\n▷ 已绑定比赛「{result.Value.Title}」。\n▷ {CompetitionUrl(competitionId)}";
        }

        if (command.Kind == BotCommandKind.Unbind)
        {
            store.CancelPendingOutbound(message.ProviderId, message.GroupId);
            var removed = store.DeleteSubscription(message.ProviderId, message.GroupId);
            subscriptions.SignalChanged();
            return removed
                ? "▌NoCTF\n▷ 已解除本群比赛绑定。"
                : "▌NoCTF\n▷ 本群当前没有绑定比赛。";
        }

        var subscription = store.GetSubscription(message.ProviderId, message.GroupId);
        if (subscription is null) return "▌NoCTF\n▷ 本群尚未绑定比赛。";
        if (command.Kind == BotCommandKind.Config) return Configuration(subscription);

        var column = command.Kind switch
        {
            BotCommandKind.Broadcasts => "broadcast_enabled",
            BotCommandKind.Scoreboard => "scoreboard_enabled",
            BotCommandKind.Blood => "blood_enabled",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        store.SetSubscriptionOption(
            message.ProviderId,
            message.GroupId,
            column,
            command.Enabled!.Value);
        if (command.Kind == BotCommandKind.Broadcasts && command.Enabled.Value)
            await ResetAnnouncementCheckpointAsync(subscription, ct);
        return $"▌NoCTF\n▷ {OptionName(command.Kind)}已{(command.Enabled.Value ? "开启" : "关闭")}。";
    }

    private async Task<string> StatusAsync(Guid competitionId, CancellationToken ct)
    {
        var result = await noCtf.GetCompetitionAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => formatter.Status(result.Value!),
            NoCtfReadState.NotFound => "▌NoCTF\n▷ 比赛当前不可访问。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "▌NoCTF\n▷ 平台暂时不可用，请稍后再试。"
        };
    }

    private async Task ResetAnnouncementCheckpointAsync(
        GroupSubscription subscription,
        CancellationToken ct)
    {
        var result = await noCtf.GetAnnouncementsAsync(subscription.CompetitionId, null, ct);
        if (result.State == NoCtfReadState.Unauthorized)
            _ = Unauthorized();
        var newest = result.State == NoCtfReadState.Available
            ? result.Value?.Items.FirstOrDefault()
            : null;
        store.SaveAnnouncementCheckpoint(new(
            subscription.ProviderId,
            subscription.GroupId,
            subscription.CompetitionId,
            newest?.PublishedAt ?? timeProvider.GetUtcNow(),
            newest?.Id ?? MaximumGuid));
    }

    private async Task<string> ChallengesAsync(Guid competitionId, CancellationToken ct)
    {
        var result = await noCtf.GetChallengesAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Challenges(result.Value!),
            NoCtfReadState.NotFound => "▌NoCTF\n▷ 比赛或题目列表当前不可访问。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "▌NoCTF\n▷ 题目列表暂时不可用，请稍后再试。"
        };
    }

    private async Task<IReadOnlyList<string>> RankAsync(
        Guid competitionId,
        string? trackKey,
        int limit,
        CancellationToken ct)
    {
        var result = await noCtf.GetLeaderboardAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Rank(result.Value!, limit, trackKey),
            NoCtfReadState.Processing =>
                [$"▌NoCTF\n▷ 排行榜正在生成，请在 {Math.Ceiling((result.RetryAfter ?? TimeSpan.FromSeconds(2)).TotalSeconds).ToString(CultureInfo.InvariantCulture)}s 后重试。"],
            NoCtfReadState.NotFound => ["▌NoCTF\n▷ 比赛当前不可访问。"],
            NoCtfReadState.Unauthorized => [Unauthorized()],
            _ => ["▌NoCTF\n▷ 排行榜投影暂时不可用，请稍后再试。"]
        };
    }

    private async Task<string> TeamAsync(Guid competitionId, string teamName, CancellationToken ct)
    {
        var result = await noCtf.GetLeaderboardAsync(competitionId, ct);
        return result.State switch
        {
            NoCtfReadState.Available => BroadcastMessageFormatter.Team(result.Value!, teamName),
            NoCtfReadState.Processing => "▌NoCTF\n▷ 排行榜正在生成，请稍后再试。",
            NoCtfReadState.Unauthorized => Unauthorized(),
            _ => "▌NoCTF\n▷ 暂时无法查询该队伍。"
        };
    }

    private string Unauthorized()
    {
        if (runtimeState.MarkNoCtfUnauthorized())
        {
            foreach (var subscription in store.GetSubscriptions(providers.Active.Id))
            {
                outbound.Enqueue(
                    $"token-invalid:{subscription.ProviderId}:{subscription.GroupId}",
                    subscription.ProviderId,
                    subscription.GroupId,
                    "▌NoCTF\n▷ BOT 凭据已失效，自动播报和查询已暂停，请联系管理员轮换 Token。");
            }
        }
        return "▌NoCTF\n▷ 平台凭据已失效，查询和播报已暂停。";
    }

    private void Reply(ChatGroupMessage message, int part, string payload) =>
        outbound.Enqueue(
            $"command:{message.ProviderId}:{message.GroupId}:{message.MessageId}:{part}",
            message.ProviderId,
            message.GroupId,
            payload);

    private string CompetitionLink(Guid competitionId) =>
        $"▌NoCTF\n▷ {CompetitionUrl(competitionId)}";

    private Uri CompetitionUrl(Guid competitionId) =>
        new(noCtfOptions.PublicBaseUrl, $"competitions/{competitionId:D}");

    private static string Configuration(GroupSubscription subscription) => $"""
        ▌NoCTF · 本群比赛配置
        ▷ 比赛：{subscription.CompetitionId:D}
        ▷ 赛事通知：{OnOff(subscription.BroadcastEnabled)}
        ▷ AWDP 榜单：{OnOff(subscription.ScoreboardEnabled)}
        ▷ 血榜：{OnOff(subscription.BloodEnabled)}
        ▷ 状态：{(subscription.SuspendedReason is null ? "正常" : "已暂停")}
        """;

    private static string OnOff(bool enabled) => enabled ? "开启" : "关闭";

    private static string OptionName(BotCommandKind kind) => kind switch
    {
        BotCommandKind.Broadcasts => "赛事通知",
        BotCommandKind.Scoreboard => "AWDP 榜单播报",
        BotCommandKind.Blood => "血榜播报",
        _ => "配置"
    };

    private const string HelpText = """
        ▌NoCTF · 可用命令
        ▷ /ctf status
        ▷ /ctf challenges
        ▷ /ctf rank [赛道] [1-20]（默认总榜）
        ▷ /ctf team <队伍名>
        ▷ /ctf link
        ▷ 管理员：enable、disable、bind <competitionId>、unbind、broadcasts on|off、scoreboard on|off、blood on|off、config
        ▷ master：admin add|remove、admins、revoke
        """;
}
