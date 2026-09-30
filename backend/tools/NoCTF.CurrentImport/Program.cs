using System.Reflection;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Persistence.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.CurrentImport;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static Program() => JsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

    public static async Task Main(string[] args)
    {
        if (args is ["--validate-converters"])
        {
            ConverterFixtures.Validate();
            Console.WriteLine("Legacy mode converter fixtures passed.");
            return;
        }
        var arguments = Arguments.Parse(args);
        using var archive = await MigrationArchive.OpenAsync(
            arguments.ArchivePath,
            arguments.KeyPath,
            CancellationToken.None);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(arguments.ConnectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync();
        await AssertEmptyAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        await AddAsync(db, archive.Rows("Files").Select(row => Populate(new StoredFile(), row)));
        await AddAsync(db, archive.Rows("Users").Select(MapUser));
        await ReplacePlatformSettingsAsync(
            db,
            archive.Rows("PlatformSettings").Select(MapPlatformSettings).Single());
        await AddAsync(db, archive.Rows("Competitions").Select(MapCompetition));
        await AddAsync(db, archive.Rows("Teams").Select(MapTeam));
        await AddAsync(db, archive.Rows("Challenges").Select(MapChallenge));
        await AddAsync(db, archive.Rows("ChallengeAttachments")
            .Select(row => Populate(new ChallengeAttachment(), row)));
        await AddAsync(db, archive.Rows("CompetitionChallenges").Select(MapCompetitionChallenge));
        await AddAsync(db, archive.Rows("ChallengeFlags").Select(MapChallengeFlag));
        await AddAsync(db, archive.Rows("GameplayFacts").Select(MapGameplayFact));
        await AddAsync(db, archive.Rows("RuntimeInstances").Select(MapRuntimeInstance));
        await AddAsync(db, archive.Rows("PatchUploads")
            .Select(row => Populate(new PatchUpload(), row)));
        await AddAsync(db, archive.Rows("CompetitionEvents").Select(MapCompetitionEvent));

        var notifications = new List<Notification>();
        var receipts = new List<CommandReceipt>();
        foreach (var row in archive.Rows("Notifications"))
        {
            if (row.Required<short>("Kind") == 21)
                receipts.Add(MapCommandReceipt(row, db));
            else
                notifications.Add(MapNotification(row));
        }
        await AddAsync(db, notifications);
        await AddAsync(db, receipts);
        await AddAsync(db, archive.Rows("AccountTokens")
            .Select(row => Populate(new AccountToken(), row)));
        await AddAsync(db, archive.Rows("DataProtectionKeys")
            .Select(row => Populate(new DataProtectionKey(), row)));

        await ValidateAsync(db, archive, arguments.StorageRoot);
        await transaction.CommitAsync();
        Console.WriteLine($"Imported archive exported at {archive.ExportedAt:O}.");
    }

    private static User MapUser(ArchiveRow row)
    {
        var user = Populate(new User(), row,
            nameof(User.ExternalIdentity),
            nameof(User.ExternalIdentityProviderId),
            nameof(User.ExternalIdentityProtocol),
            nameof(User.ExternalIdentityNamespace),
            nameof(User.ExternalIdentitySubject),
            nameof(User.ExternalIdentityBoundAt));
        if (!IsIdentityV3(user.PasswordHash))
            throw new InvalidOperationException($"User {user.Id} has a non-Identity-V3 password hash.");
        if (row.Optional<Guid?>("ExternalIdentityProviderId") is Guid providerId)
        {
            user.ExternalIdentity = new ExternalIdentity
            {
                UserId = user.Id,
                ProviderId = providerId,
                Protocol = row.Required<SsoProtocol>("ExternalIdentityProtocol"),
                IdentityNamespace = row.Required<string>("ExternalIdentityNamespace"),
                Subject = row.Required<string>("ExternalIdentitySubject"),
                BoundAt = row.Required<DateTimeOffset>("ExternalIdentityBoundAt")
            };
        }
        return user;
    }

    private static bool IsIdentityV3(string hash)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length < 13 || payload[0] != 0x01)
            return false;
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(5, 4));
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(9, 4));
        return iterations > 0
            && saltLength >= 16
            && saltLength <= payload.Length - 13
            && payload.Length - 13 - saltLength >= 16;
    }

    private static PlatformSettings MapPlatformSettings(ArchiveRow row)
    {
        var settings = Populate(new PlatformSettings(), row,
            nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames),
            nameof(PlatformSettings.SsoConfiguration));
        settings.HumanVerificationTurnstileAllowedHostnames =
            row.Optional<string[]>("HumanVerificationTurnstileAllowedHostnames") ?? [];
        var sso = row.Element.GetProperty("SsoConfiguration");
        settings.SsoConfiguration = LegacyConverters.Sso(sso);
        return settings;
    }

    private static Competition MapCompetition(ArchiveRow row)
    {
        var mode = row.Required<GameMode>("Mode");
        if (!Enum.IsDefined(mode))
            throw new InvalidOperationException($"Production competition {row.Id} has an unknown mode.");
        var competition = Populate(CompetitionGeneratedCatalog.Create(mode), row,
            nameof(Competition.Mode),
            nameof(Competition.ManagerIds),
            nameof(Competition.JudgeIds),
            nameof(Competition.ObserverIds),
            nameof(Competition.Tracks),
            nameof(Competition.WebhookConfiguration),
            nameof(Competition.ModeConfiguration));
        competition.ManagerIds = row.Optional<Guid[]>("ManagerIds") ?? [];
        competition.JudgeIds = row.Optional<Guid[]>("JudgeIds") ?? [];
        competition.ObserverIds = row.Optional<Guid[]>("ObserverIds") ?? [];
        competition.ModeConfiguration = LegacyConverters.Competition(
            competition.Id,
            mode,
            row.Required<string>("ConfigurationJson"));
        competition.Tracks = CompetitionTrackConfiguration.ToPersisted(
            LegacyConverters.Tracks(mode, row.Text("TrackConfigurationJson")),
            competition.Id);
        var webhooks = row.Element.GetProperty("WebhookConfiguration")
            .Deserialize<CompetitionWebhookConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Webhook configuration cannot be null.");
        foreach (var target in webhooks.Targets)
            target.CompetitionId = competition.Id;
        competition.WebhookConfiguration = webhooks;
        return competition;
    }

    private static Team MapTeam(ArchiveRow row)
    {
        var team = Populate(new Team(), row,
            nameof(Team.MemberIds),
            nameof(Team.Members),
            nameof(Team.CaptainMembership));
        team.MemberIds = row.Optional<Guid[]>("MemberIds") ?? [];
        return team;
    }

    private static Challenge MapChallenge(ArchiveRow row)
    {
        var mode = row.Required<GameMode>("Mode");
        if (!Enum.IsDefined(mode))
            throw new InvalidOperationException($"Production challenge {row.Id} has an unknown mode.");
        var challenge = Populate(ChallengeGeneratedCatalog.Create(mode), row,
            nameof(Challenge.Mode),
            nameof(Challenge.ManagerIds),
            nameof(Challenge.Managers),
            nameof(Challenge.Definition),
            nameof(Challenge.Attachments));
        challenge.ManagerIds = row.Optional<Guid[]>("ManagerIds") ?? [];
        challenge.Definition = LegacyConverters.Definition(
            challenge.Id,
            mode,
            row.Required<string>("DefinitionJson"));
        return challenge;
    }

    private static CompetitionChallenge MapCompetitionChallenge(ArchiveRow row)
    {
        var mode = row.Required<GameMode>("Mode");
        if (!Enum.IsDefined(mode))
            throw new InvalidOperationException($"Competition challenge {row.Id} has an unknown mode.");
        var challenge = Populate(CompetitionChallengeGeneratedCatalog.Create(mode), row,
            nameof(CompetitionChallenge.Mode),
            nameof(CompetitionChallenge.Rules),
            nameof(CompetitionChallenge.Hints));
        challenge.Rules = LegacyConverters.Rules(
            challenge.Id,
            mode,
            row.Required<string>("RulesJson"));
        challenge.Hints = row.Element.GetProperty("Hints")
            .Deserialize<List<CompetitionChallengeHint>>(JsonOptions) ?? [];
        return challenge;
    }

    private static ChallengeFlag MapChallengeFlag(ArchiveRow row)
    {
        var challengeId = row.Optional<Guid?>("ChallengeId");
        var competitionChallengeId = row.Optional<Guid?>("CompetitionChallengeId");
        var teamId = row.Optional<Guid?>("TeamId");
        var specification = row.Optional<SpecificationKind?>("SpecificationKind");
        ChallengeFlag flag = specification switch
        {
            SpecificationKind.AwdRound => new AwdRoundChallengeFlag(),
            SpecificationKind.RuntimeInstance => new RuntimeInstanceChallengeFlag(),
            _ when challengeId is not null => new TemplateChallengeFlag(),
            _ when competitionChallengeId is not null && teamId is not null => new TeamChallengeFlag(),
            _ when competitionChallengeId is not null => new CompetitionChallengeFlag(),
            _ => throw new InvalidOperationException($"Challenge flag {row.Id} has no valid scope.")
        };
        return Populate(flag, row, nameof(ChallengeFlag.Type), nameof(ChallengeFlag.SpecificationIdentity));
    }

    private static GameplayFact MapGameplayFact(ArchiveRow row)
    {
        var kind = row.Required<GameplayFactKind>("Kind");
        var fact = GameplayFactGeneratedCatalog.Create(kind);
        return Populate(fact, row, nameof(GameplayFact.Kind));
    }

    private static RuntimeInstance MapRuntimeInstance(ArchiveRow row)
    {
        var purpose = row.Required<RuntimePurpose>("Purpose");
        var runtime = Populate(RuntimeInstanceGeneratedCatalog.Create(purpose), row,
            nameof(RuntimeInstance.Purpose),
            nameof(RuntimeInstance.ActiveSlot),
            nameof(RuntimeInstance.ProviderReceipt),
            nameof(RuntimeInstance.CapacityAllocations),
            nameof(RuntimeInstance.CapacityAllocationEntries),
            nameof(RuntimeInstance.AccessEndpoints),
            nameof(RuntimeInstance.PublishedPorts));
        runtime.AccessEndpoints = row.Element.GetProperty("AccessEndpoints")
            .Deserialize<List<RuntimeAccessEndpoint>>(JsonOptions) ?? [];
        runtime.PublishedPorts = row.Element.GetProperty("PublishedPorts")
            .Deserialize<List<RuntimePublishedPort>>(JsonOptions) ?? [];
        var allocations = row.Element.GetProperty("CapacityAllocations")
            .Deserialize<RuntimeCapacityAllocations>(JsonOptions) ?? RuntimeCapacityAllocations.Empty;
        runtime.CapacityAllocationEntries = allocations.Items
            .Select(RuntimeCapacityAllocationEntry.FromValue).ToList();
        if (row.Text("ProviderReceiptJson") is { Length: > 0 } receipt)
            runtime.ProviderReceipt = Receipt(runtime, receipt);
        return runtime;
    }

    private static RuntimeReceipt Receipt(RuntimeInstance runtime, string json) =>
        runtime.RuntimeKind switch
        {
            RuntimeKind.Container => ContainerRuntimeReceiptData.From(
                    JsonSerializer.Deserialize<ContainerDeploymentReceipt>(json, JsonOptions)
                    ?? throw new InvalidOperationException($"Runtime {runtime.Id} receipt is invalid."))
                .ToEntity(runtime.Id),
            RuntimeKind.OvaVm => OvaRuntimeReceiptData.From(
                    JsonSerializer.Deserialize<OvaRuntimeReceipt>(json, JsonOptions)
                    ?? throw new InvalidOperationException($"Runtime {runtime.Id} receipt is invalid."))
                .ToEntity(runtime.Id),
            _ => throw new InvalidOperationException($"Runtime {runtime.Id} has unknown kind.")
        };

    private static CompetitionEvent MapCompetitionEvent(ArchiveRow row)
    {
        var kind = row.Required<CompetitionEventKind>("Kind");
        var item = Populate(CompetitionEventGeneratedCatalog.Create(kind), row,
            nameof(CompetitionEvent.Kind));
        using var payload = JsonDocument.Parse(row.Required<string>("PayloadJson"));
        ApplyPayload(item, payload.RootElement);
        item.RelatedUserId ??= Reference(item, EntityReferenceKind.User);
        item.TeamId ??= Reference(item, EntityReferenceKind.Team);
        item.CompetitionChallengeId ??= Reference(item, EntityReferenceKind.CompetitionChallenge);
        item.HintId ??= Reference(item, EntityReferenceKind.ChallengeHint);
        item.RuntimeInstanceId ??= Reference(item, EntityReferenceKind.RuntimeInstance);
        item.GameplayFactId ??= Reference(item, EntityReferenceKind.GameplayFact);
        item.QuestionId ??= Reference(item, EntityReferenceKind.Notification);
        return item;
    }

    private static Notification MapNotification(ArchiveRow row)
    {
        var oldKind = row.Required<short>("Kind");
        var kind = (NotificationKind)(oldKind > 21 ? oldKind - 1 : oldKind);
        if (!Enum.IsDefined(kind))
            throw new InvalidOperationException($"Unknown notification kind {oldKind}.");
        var item = Populate(NotificationGeneratedCatalog.Create(kind), row,
            nameof(Notification.Kind));
        using var content = JsonDocument.Parse(row.Required<string>("ContentJson"));
        ApplyContent(item, content.RootElement);
        return item;
    }

    private static CommandReceipt MapCommandReceipt(ArchiveRow row, NoCtfDbContext db)
    {
        using var content = JsonDocument.Parse(row.Required<string>("ContentJson"));
        var root = content.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException($"Command receipt {row.Id} has an unknown schema.");
        var result = root.GetProperty("result");
        ReplayOperation operation;
        Guid resourceId;
        if (result.ValueKind == JsonValueKind.Array)
        {
            operation = ReplayOperation.FlagSubmission;
            var factId = result.EnumerateArray()
                .Select(value => OptionalGuid(value, "gameplayFactId"))
                .FirstOrDefault(value => value is not null)
                ?? throw new InvalidOperationException($"Command receipt {row.Id} has no fact id.");
            resourceId = db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.Id == factId)
                .Select(fact => fact.CompetitionChallengeId)
                .Single();
        }
        else if (result.TryGetProperty("PatchUploadId", out _)
                 || result.TryGetProperty("patchUploadId", out _))
        {
            operation = ReplayOperation.PatchUpload;
            var patchUploadId = GuidValue(result, "patchUploadId");
            resourceId = db.PatchUploads.AsNoTracking()
                .Where(upload => upload.Id == patchUploadId)
                .Select(upload => upload.RuntimeInstanceId)
                .Single() ?? throw new InvalidOperationException(
                    $"Patch upload receipt {row.Id} has no runtime scope.");
        }
        else if (result.TryGetProperty("GameplayFactId", out _)
                 || result.TryGetProperty("gameplayFactId", out _))
        {
            operation = ReplayOperation.ManualAdjustment;
            var factId = GuidValue(result, "gameplayFactId");
            resourceId = db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.Id == factId)
                .Select(fact => fact.CompetitionChallengeId)
                .Single();
        }
        else if (result.TryGetProperty("RuntimeInstanceId", out _)
                 || result.TryGetProperty("runtimeInstanceId", out _))
        {
            var runtimeId = GuidValue(result, "runtimeInstanceId");
            var runtime = db.RuntimeInstances.AsNoTracking()
                .Where(item => item.Id == runtimeId)
                .Select(item => new
                {
                    item.CompetitionChallengeId, item.ChallengeId,
                    item.TeamId, item.Purpose
                })
                .Single();
            resourceId = runtime.CompetitionChallengeId ?? runtime.ChallengeId
                ?? throw new InvalidOperationException($"Runtime receipt {row.Id} has no resource scope.");
            if (result.TryGetProperty("State", out _)
                || result.TryGetProperty("state", out _))
                operation = runtime.Purpose switch
                {
                    RuntimePurpose.AwdpTarget => ReplayOperation.AwdpDefenseTarget,
                    RuntimePurpose.PatchVerificationTarget => ReplayOperation.PatchVerificationTarget,
                    _ => throw new InvalidOperationException(
                        $"Runtime receipt {row.Id} has an unsupported result purpose.")
                };
            else if (row.Optional<Guid?>("RelatedId") is not Guid competitionId)
                operation = ReplayOperation.TemplateTestRuntimeMutation;
            else if (runtime.TeamId is Guid teamId
                     && db.Set<TeamMember>().Any(member => member.TeamId == teamId
                         && member.UserId == row.Required<Guid>("SourceId")))
                operation = ReplayOperation.RuntimeMutation;
            else
                operation = ReplayOperation.AdminRuntimeMutation;
        }
        else
        {
            throw new InvalidOperationException(
                $"Command receipt {row.Id} has an unsupported result shape.");
        }

        var receipt = CommandReceiptGeneratedCatalog.Create(operation);
        receipt.Id = row.Id;
        receipt.UserId = row.Required<Guid>("SourceId");
        receipt.CompetitionId = row.Optional<Guid?>("RelatedId") ?? Guid.Empty;
        receipt.ResourceId = resourceId;
        receipt.InputFingerprint = Convert.FromHexString(
            root.GetProperty("fingerprint").GetString()
            ?? throw new InvalidOperationException($"Command receipt {row.Id} has no fingerprint."));
        receipt.CommittedAt = row.Required<DateTimeOffset>("SentAt");
        WriteReceiptResult(receipt, result);
        return receipt;
    }

    private static void WriteReceiptResult(CommandReceipt receipt, JsonElement result)
    {
        switch (receipt.Operation)
        {
            case ReplayOperation.FlagSubmission:
                receipt.GameplayFactResults = result.EnumerateArray().Select((item, position) =>
                    new CommandReceiptGameplayFactResult
                    {
                        CommandReceiptId = receipt.Id,
                        Position = position,
                        State = (short)EnumValue<GameplayFactAcceptanceState>(item, "state"),
                        GameplayFactId = OptionalGuid(item, "gameplayFactId"),
                        OccurredAt = OptionalDateTime(item, "occurredAt")
                    }).ToList();
                break;
            case ReplayOperation.ManualAdjustment:
                receipt.ResultState = (short)EnumValue<GameplayFactAcceptanceState>(result, "state");
                receipt.PrimaryResultId = OptionalGuid(result, "gameplayFactId");
                receipt.ResultOccurredAt = OptionalDateTime(result, "occurredAt");
                break;
            case ReplayOperation.PatchUpload:
                receipt.PrimaryResultId = GuidValue(result, "patchUploadId");
                receipt.SecondaryResultId = GuidValue(result, "gameplayFactId");
                receipt.GameplayFactState = EnumValue<GameplayFactState>(result, "state");
                break;
            case ReplayOperation.AwdpDefenseTarget:
                receipt.ResultState = (short)EnumValue<AwdpDefenseTargetRequestState>(result, "state");
                receipt.PrimaryResultId = OptionalGuid(result, "runtimeInstanceId");
                receipt.RuntimeState = OptionalEnum<RuntimeState>(result, "runtimeState");
                break;
            case ReplayOperation.PatchVerificationTarget:
                receipt.ResultState = (short)EnumValue<PatchVerificationTargetRequestState>(result, "state");
                receipt.PrimaryResultId = OptionalGuid(result, "runtimeInstanceId");
                receipt.RuntimeState = OptionalEnum<RuntimeState>(result, "runtimeState");
                break;
            case ReplayOperation.RuntimeMutation:
            case ReplayOperation.AdminRuntimeMutation:
            case ReplayOperation.TemplateTestRuntimeMutation:
                receipt.PrimaryResultId = GuidValue(result, "runtimeInstanceId");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported migrated receipt operation {receipt.Operation}.");
        }
    }

    private static void ApplyPayload(CompetitionEvent item, JsonElement payload)
    {
        RequireVersion(payload, 1, item.Id);
        ApplyMatchingProperties(item, payload, "schemaVersion", "from", "to", "enabled",
            "trackKeys", "segmentId", "bindingIndex", "connectionId", "startedAt", "endedAt",
            "clientAddress", "clientPort", "destinationAddress", "destinationPort",
            "clientToRuntimeBytes", "runtimeToClientBytes", "capturedBytes", "truncated");
        item.Automatic = OptionalBool(payload, "automatic") ?? item.Automatic;
        if (item.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
        {
            item.PreviousCompetitionStatus = OptionalEnum<CompetitionStatus>(payload, "from");
            item.CompetitionStatus = OptionalEnum<CompetitionStatus>(payload, "to")
                ?? item.CompetitionStatus;
        }
        if (item.Kind == CompetitionEventKind.LeaderboardVisibilityChanged)
        {
            item.PreviousLeaderboardVisibility = OptionalEnum<CompetitionLeaderboardVisibility>(payload, "from");
            item.LeaderboardVisibility = OptionalEnum<CompetitionLeaderboardVisibility>(payload, "to")
                ?? item.LeaderboardVisibility;
        }
        item.TrackConfigurationEnabled = OptionalBool(payload, "enabled");
        item.TrackKeys = OptionalStrings(payload, "trackKeys").Select((value, position) =>
            new CompetitionEventTrackKey
            {
                Id = Guid.CreateVersion7(item.OccurredAt.AddTicks(position)),
                Position = position,
                Value = value
            }).ToList();
        item.TrafficSegmentId = OptionalGuid(payload, "segmentId");
        item.TrafficBindingIndex = OptionalInt(payload, "bindingIndex");
        item.TrafficConnectionId = OptionalString(payload, "connectionId");
        item.TrafficStartedAt = OptionalDateTime(payload, "startedAt");
        item.TrafficEndedAt = OptionalDateTime(payload, "endedAt");
        item.TrafficClientAddress = OptionalString(payload, "clientAddress");
        item.TrafficClientPort = OptionalInt(payload, "clientPort");
        item.TrafficDestinationAddress = OptionalString(payload, "destinationAddress");
        item.TrafficDestinationPort = OptionalInt(payload, "destinationPort");
        item.TrafficClientToRuntimeBytes = OptionalLong(payload, "clientToRuntimeBytes");
        item.TrafficRuntimeToClientBytes = OptionalLong(payload, "runtimeToClientBytes");
        item.TrafficCapturedBytes = OptionalLong(payload, "capturedBytes");
        item.TrafficTruncated = OptionalBool(payload, "truncated");
    }

    private static void ApplyContent(Notification item, JsonElement content)
    {
        RequireVersion(content, 1, item.Id);
        ApplyMatchingProperties(item, content, "schemaVersion", "id", "kind",
            "sourceType", "sourceId", "targetType", "targetId", "relatedType",
            "relatedId", "sentAt", "threadRootId", "replyToId", "sourceEventKey",
            "from", "to", "status",
            "actorRole", "event", "action", "protocol", "providerId",
            "targetUserId", "targetUserName", "tokenVersion", "bloodRank", "cost",
            "publishedAt", "bannedAt", "detectedAt", "correctedAt");
        item.PreviousQuestionStatus = OptionalEnum<CompetitionQuestionStatus>(content, "from");
        item.QuestionStatus = OptionalEnum<CompetitionQuestionStatus>(content, "to")
            ?? OptionalEnum<CompetitionQuestionStatus>(content, "status")
            ?? item.QuestionStatus;
        item.QuestionActorRole = OptionalEnum<CompetitionQuestionParticipantRole>(content, "actorRole");
        item.ActionValue = OptionalInt(content, "kind")
            ?? OptionalInt(content, "action")
            ?? OptionalInt(content, "bloodRank");
        item.SsoProtocol = OptionalEnum<SsoProtocol>(content, "protocol");
        item.SsoProviderId ??= OptionalGuid(content, "providerId");
        item.UserId ??= OptionalGuid(content, "targetUserId");
        item.UserName ??= OptionalString(content, "targetUserName");
        item.Value = OptionalLong(content, "cost") ?? OptionalLong(content, "tokenVersion");
        item.PayloadOccurredAt = OptionalDateTime(content, "occurredAt")
            ?? OptionalDateTime(content, "publishedAt")
            ?? OptionalDateTime(content, "bannedAt")
            ?? OptionalDateTime(content, "detectedAt")
            ?? OptionalDateTime(content, "correctedAt");
        item.PayloadExpiresAt = OptionalDateTime(content, "expiresAt");
    }

    private static void ApplyMatchingProperties(
        object target,
        JsonElement source,
        params string[] excluded)
    {
        var exclusions = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite
                && property.SetMethod?.IsPublic == true
                && property.GetIndexParameters().Length == 0)
            .ToArray();
        foreach (var sourceProperty in source.EnumerateObject())
        {
            if (exclusions.Contains(sourceProperty.Name)
                || sourceProperty.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            var targetProperty = properties.FirstOrDefault(property =>
                string.Equals(property.Name, sourceProperty.Name, StringComparison.OrdinalIgnoreCase));
            if (targetProperty is null || targetProperty.PropertyType.IsGenericType
                && targetProperty.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                continue;
            targetProperty.SetValue(target,
                sourceProperty.Value.Deserialize(targetProperty.PropertyType, JsonOptions));
        }
    }

    private static Guid? Reference(CompetitionEvent item, EntityReferenceKind kind) =>
        item.SubjectType == kind ? item.SubjectId
            : item.RelatedType == kind ? item.RelatedId : null;

    private static T Populate<T>(T target, ArchiveRow row, params string[] excluded)
        where T : class
    {
        var exclusions = excluded.ToHashSet(StringComparer.Ordinal);
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.SetMethod?.IsPublic != true
                || property.GetIndexParameters().Length != 0
                || exclusions.Contains(property.Name)
                || !row.Element.TryGetProperty(property.Name, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            property.SetValue(target, value.Deserialize(property.PropertyType, JsonOptions));
        }
        return target;
    }

    private static async Task AddAsync<T>(NoCtfDbContext db, IEnumerable<T> entities)
        where T : class
    {
        db.Set<T>().AddRange(entities);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task ReplacePlatformSettingsAsync(
        NoCtfDbContext db,
        PlatformSettings imported)
    {
        var seeded = await db.PlatformSettings.SingleAsync(settings => settings.Id == imported.Id);
        db.PlatformSettings.Remove(seeded);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.PlatformSettings.Add(imported);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task AssertEmptyAsync(NoCtfDbContext db)
    {
        if (await db.Users.AnyAsync()
            || await db.Competitions.IgnoreQueryFilters().AnyAsync()
            || await db.Challenges.IgnoreQueryFilters().AnyAsync()
            || await db.Notifications.AnyAsync()
            || await db.GameplayFacts.AnyAsync())
            throw new InvalidOperationException("The target database is not empty.");
    }

    private static async Task ValidateAsync(
        NoCtfDbContext db,
        MigrationArchive archive,
        string? storageRoot)
    {
        var checks = new Dictionary<string, int>
        {
            ["Users"] = await db.Users.CountAsync(),
            ["Competitions"] = await db.Competitions.IgnoreQueryFilters().CountAsync(),
            ["Teams"] = await db.Teams.IgnoreQueryFilters().CountAsync(),
            ["Challenges"] = await db.Challenges.IgnoreQueryFilters().CountAsync(),
            ["CompetitionChallenges"] = await db.CompetitionChallenges.IgnoreQueryFilters().CountAsync(),
            ["ChallengeFlags"] = await db.ChallengeFlags.IgnoreQueryFilters().CountAsync(),
            ["RuntimeInstances"] = await db.RuntimeInstances.CountAsync(),
            ["GameplayFacts"] = await db.GameplayFacts.CountAsync(),
            ["CompetitionEvents"] = await db.CompetitionEvents.CountAsync(),
            ["Files"] = await db.Files.CountAsync(),
            ["AccountTokens"] = await db.AccountTokens.CountAsync(),
            ["ChallengeAttachments"] = await db.Set<ChallengeAttachment>().CountAsync(),
            ["PatchUploads"] = await db.PatchUploads.CountAsync(),
            ["PlatformSettings"] = await db.PlatformSettings.CountAsync(),
            ["DataProtectionKeys"] = await db.DataProtectionKeys.CountAsync()
        };
        foreach (var (name, count) in checks)
        {
            var expected = archive.Rows(name).Count;
            if (count != expected)
                throw new InvalidOperationException(
                    $"Imported {name} count {count} does not match archive count {expected}.");
        }
        var oldNotifications = archive.Rows("Notifications");
        var expectedReceipts = oldNotifications.Count(row => row.Required<short>("Kind") == 21);
        var expectedNotifications = oldNotifications.Count - expectedReceipts;
        if (await db.CommandReceipts.CountAsync() != expectedReceipts
            || await db.Notifications.CountAsync() != expectedNotifications)
            throw new InvalidOperationException("Notification/command receipt counts do not match.");
        await AssertIdsAsync("Users", db.Users.Select(item => item.Id), archive);
        await AssertIdsAsync("Competitions", db.Competitions.IgnoreQueryFilters()
            .Select(item => item.Id), archive);
        await AssertIdsAsync("Teams", db.Teams.IgnoreQueryFilters()
            .Select(item => item.Id), archive);
        await AssertIdsAsync("Challenges", db.Challenges.IgnoreQueryFilters()
            .Select(item => item.Id), archive);
        await AssertIdsAsync("CompetitionChallenges",
            db.CompetitionChallenges.IgnoreQueryFilters().Select(item => item.Id), archive);
        await AssertIdsAsync("ChallengeFlags", db.ChallengeFlags.IgnoreQueryFilters()
            .Select(item => item.Id), archive);
        await AssertIdsAsync("RuntimeInstances", db.RuntimeInstances.Select(item => item.Id), archive);
        await AssertIdsAsync("GameplayFacts", db.GameplayFacts.Select(item => item.Id), archive);
        await AssertIdsAsync("CompetitionEvents", db.CompetitionEvents.Select(item => item.Id), archive);
        await AssertIdsAsync("Files", db.Files.Select(item => item.Id), archive);
        await AssertIdsAsync("AccountTokens", db.AccountTokens.Select(item => item.Id), archive);
        await AssertIdsAsync("ChallengeAttachments", db.Set<ChallengeAttachment>()
            .Select(item => item.Id), archive);
        await AssertIdsAsync("PatchUploads", db.PatchUploads.Select(item => item.Id), archive);
        var oldMessages = archive.Rows("Notifications");
        await AssertIdSetAsync("Notifications", db.Notifications.Select(item => item.Id),
            oldMessages.Where(row => row.Required<short>("Kind") != 21).Select(row => row.Id));
        await AssertIdSetAsync("CommandReceipts", db.CommandReceipts.Select(item => item.Id),
            oldMessages.Where(row => row.Required<short>("Kind") == 21).Select(row => row.Id));
        var oldSsoProviderCount = archive.Rows("PlatformSettings").Single()
            .Element.GetProperty("SsoConfiguration").GetProperty("providers").GetArrayLength();
        if (await db.Set<SsoProviderConfiguration>().CountAsync() != oldSsoProviderCount)
            throw new InvalidOperationException("SSO provider count does not match the archive.");
        var importedSettings = await db.PlatformSettings.AsNoTracking().SingleAsync();
        var oldSettings = archive.Rows("PlatformSettings").Single();
        AssertSharedScalarContent(importedSettings, oldSettings, "PlatformSettings");
        if (!importedSettings.HumanVerificationTurnstileAllowedHostnames.SequenceEqual(
                oldSettings.Optional<string[]>("HumanVerificationTurnstileAllowedHostnames") ?? []))
            throw new InvalidOperationException(
                "Imported human verification hostnames differ from the archive.");
        ValidateSsoContent(importedSettings.SsoConfiguration,
            LegacyConverters.Sso(oldSettings.Element.GetProperty("SsoConfiguration")));
        await ValidateIdentityAndFileContentAsync(db, archive);
        if (storageRoot is not null)
            await ValidateStoredObjectsAsync(db, storageRoot);
        await ValidateSharedScalarContentAsync("Users", db.Users.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("Competitions",
            db.Competitions.IgnoreQueryFilters().AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("Teams",
            db.Teams.IgnoreQueryFilters().AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("Challenges",
            db.Challenges.IgnoreQueryFilters().AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("CompetitionChallenges",
            db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("ChallengeFlags",
            db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("RuntimeInstances",
            db.RuntimeInstances.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("GameplayFacts",
            db.GameplayFacts.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("CompetitionEvents",
            db.CompetitionEvents.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("Files", db.Files.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("AccountTokens",
            db.AccountTokens.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("PatchUploads",
            db.PatchUploads.AsNoTracking(), archive);
        await ValidateSharedScalarContentAsync("Notifications",
            db.Notifications.AsNoTracking(), archive,
            oldMessages.Where(row => row.Required<short>("Kind") != 21),
            ["Kind"]);
        if (await db.Teams.AnyAsync(team => !team.Members.Any(member => member.UserId == team.CaptainId)))
            throw new InvalidOperationException("A migrated team captain is not a member.");
        if (await db.RuntimeInstances.AnyAsync(runtime => runtime.State == RuntimeState.Running
                && runtime.ProviderReceipt == null))
            throw new InvalidOperationException("A running runtime has no typed receipt.");

        using var cacheServices = new ServiceCollection()
            .AddFusionCache(NoCtfCacheNames.Leaderboards)
            .Services
            .BuildServiceProvider();
        var cache = new FusionLeaderboardCache(
            db,
            new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            new NoOpLeaderboardRefreshPublisher(),
            cacheServices.GetRequiredService<IFusionCacheProvider>());
        foreach (var competitionId in await db.Competitions.IgnoreQueryFilters()
                     .Select(competition => competition.Id).ToArrayAsync())
        {
            if (await cache.CreateScoreboardAsync(
                    competitionId,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None) is null)
                throw new InvalidOperationException(
                    $"Competition {competitionId} could not rebuild its scoreboard.");
        }
    }

    private static Task AssertIdsAsync(
        string name,
        IQueryable<Guid> importedIds,
        MigrationArchive archive) =>
        AssertIdSetAsync(name, importedIds, archive.Rows(name).Select(row => row.Id));

    private static async Task AssertIdSetAsync(
        string name,
        IQueryable<Guid> importedIds,
        IEnumerable<Guid> expectedIds)
    {
        var expected = expectedIds.Order().ToArray();
        var actual = (await importedIds.ToArrayAsync()).Order().ToArray();
        if (!expected.SequenceEqual(actual))
        {
            var missing = expected.Except(actual).Take(3).ToArray();
            var unexpected = actual.Except(expected).Take(3).ToArray();
            throw new InvalidOperationException(
                $"Imported {name} ids do not match the archive "
                + $"(missing {expected.Except(actual).Count()}, unexpected {actual.Except(expected).Count()}; "
                + $"samples missing={string.Join(',', missing)}, unexpected={string.Join(',', unexpected)}).");
        }
    }

    private static async Task ValidateIdentityAndFileContentAsync(
        NoCtfDbContext db,
        MigrationArchive archive)
    {
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(user => user.Id);
        foreach (var row in archive.Rows("Users"))
        {
            if (!users.TryGetValue(row.Id, out var user)
                || user.UserName != row.Required<string>("UserName")
                || user.Email != row.Required<string>("Email")
                || user.PasswordHash != row.Required<string>("PasswordHash")
                || user.TokenVersion != row.Required<int>("TokenVersion")
                || user.EmailVerifiedAt?.UtcTicks
                    != row.Optional<DateTimeOffset?>("EmailVerifiedAt")?.UtcTicks)
                throw new InvalidOperationException("Imported user identity content differs from the archive.");
        }

        var files = await db.Files.AsNoTracking().ToDictionaryAsync(file => file.Id);
        foreach (var row in archive.Rows("Files"))
        {
            if (!files.TryGetValue(row.Id, out var file)
                || file.ObjectKey != row.Required<string>("ObjectKey")
                || file.FileName != row.Required<string>("FileName")
                || file.ContentType != row.Required<string>("ContentType")
                || file.ByteLength != row.Required<long>("ByteLength")
                || !file.Sha256.SequenceEqual(row.Required<byte[]>("Sha256")))
                throw new InvalidOperationException("Imported file metadata differs from the archive.");
        }

        var keys = await db.DataProtectionKeys.AsNoTracking()
            .ToDictionaryAsync(key => key.Id);
        foreach (var row in archive.Rows("DataProtectionKeys"))
        {
            if (!keys.TryGetValue(row.Required<int>("Id"), out var key)
                || key.FriendlyName != row.Text("FriendlyName")
                || key.Xml != row.Required<string>("Xml"))
                throw new InvalidOperationException("Imported Data Protection key differs from the archive.");
        }
    }

    private static async Task ValidateStoredObjectsAsync(
        NoCtfDbContext db,
        string storageRoot)
    {
        var root = Path.GetFullPath(storageRoot);
        if (!Directory.Exists(root))
            throw new InvalidOperationException("Object storage root does not exist.");
        var rootPrefix = Path.TrimEndingDirectorySeparator(root)
            + Path.DirectorySeparatorChar;
        foreach (var file in await db.Files.AsNoTracking().ToArrayAsync())
        {
            if (Path.IsPathRooted(file.ObjectKey))
                throw new InvalidOperationException("Stored object key is not relative.");
            var path = Path.GetFullPath(Path.Combine(root, file.ObjectKey));
            if (!path.StartsWith(rootPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Stored object key escapes storage root.");
            var information = new FileInfo(path);
            if (!information.Exists || information.Length != file.ByteLength)
                throw new InvalidOperationException(
                    $"Stored object {file.Id} is missing or has a different length.");
            await using var stream = information.OpenRead();
            var hash = await SHA256.HashDataAsync(stream);
            if (!hash.SequenceEqual(file.Sha256))
                throw new InvalidOperationException(
                    $"Stored object {file.Id} differs from its database SHA-256.");
        }
    }

    private static async Task ValidateSharedScalarContentAsync<T>(
        string name,
        IQueryable<T> importedQuery,
        MigrationArchive archive,
        IEnumerable<ArchiveRow>? expectedRows = null,
        HashSet<string>? excludedProperties = null)
        where T : class
    {
        var imported = (await importedQuery.ToArrayAsync()).ToDictionary(item =>
            (Guid)(item.GetType().GetProperty("Id")?.GetValue(item)
                ?? throw new InvalidOperationException($"{name} has no ID.")));
        foreach (var row in expectedRows ?? archive.Rows(name))
        {
            if (!imported.TryGetValue(row.Id, out var item))
                throw new InvalidOperationException($"Imported {name} row is missing.");
            AssertSharedScalarContent(item, row, name, excludedProperties);
        }
    }

    private static void AssertSharedScalarContent(
        object item,
        ArchiveRow row,
        string name,
        HashSet<string>? excludedProperties = null)
    {
        foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name is "ConcurrencyStamp" or "SpecificationIdentity"
                || excludedProperties?.Contains(property.Name) == true
                || property.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>()
                    is not null
                || !IsScalar(property.PropertyType)
                || !row.Element.TryGetProperty(property.Name, out var oldValue))
                continue;
            var expected = oldValue.Deserialize(property.PropertyType, JsonOptions);
            var actual = property.GetValue(item);
            var equal = expected is byte[] oldBytes && actual is byte[] newBytes
                ? oldBytes.SequenceEqual(newBytes)
                : Equals(expected, actual);
            if (!equal)
            {
                if (name == "Notifications" && property.Name == nameof(Notification.ThreadRootId))
                    throw new InvalidOperationException(
                        $"Imported notification thread root differs at {row.IdText}: "
                        + $"archive={expected}, imported={actual}.");
                throw new InvalidOperationException(
                    $"Imported {name}.{property.Name} differs from the archive at {row.IdText}.");
            }
        }
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(string) || type == typeof(Guid)
            || type == typeof(DateTimeOffset) || type == typeof(byte[])
            || type == typeof(bool) || type == typeof(short) || type == typeof(int)
            || type == typeof(long) || type == typeof(decimal) || type == typeof(double);
    }

    private static void ValidateSsoContent(SsoConfiguration actual, SsoConfiguration expected)
    {
        if (actual.Enabled != expected.Enabled
            || actual.PublicBaseUrl != expected.PublicBaseUrl
            || actual.Providers.Count != expected.Providers.Count)
            throw new InvalidOperationException("Imported SSO settings differ from the archive.");
        var providers = actual.Providers.ToDictionary(provider => provider.Id);
        foreach (var source in expected.Providers)
        {
            if (!providers.TryGetValue(source.Id, out var target)
                || target.Protocol != source.Protocol
                || target.Name != source.Name
                || target.IconUrl != source.IconUrl
                || target.Enabled != source.Enabled
                || target.AllowLogin != source.AllowLogin
                || target.AllowBinding != source.AllowBinding
                || target.TimeoutSeconds != source.TimeoutSeconds
                || !target.AllowedHosts.SequenceEqual(source.AllowedHosts))
                throw new InvalidOperationException("Imported SSO provider differs from the archive.");
            if (source is OidcSsoProviderConfiguration expectedOidc)
            {
                if (target is not OidcSsoProviderConfiguration actualOidc
                    || actualOidc.Issuer != expectedOidc.Issuer
                    || actualOidc.DiscoveryUrl != expectedOidc.DiscoveryUrl
                    || actualOidc.ClientId != expectedOidc.ClientId
                    || !NullableBytesEqual(actualOidc.ClientSecretCiphertext,
                        expectedOidc.ClientSecretCiphertext)
                    || !actualOidc.Scopes.SequenceEqual(expectedOidc.Scopes)
                    || actualOidc.ReadUserInfo != expectedOidc.ReadUserInfo
                    || actualOidc.DisplayNameClaim != expectedOidc.DisplayNameClaim)
                    throw new InvalidOperationException("Imported OIDC settings differ from the archive.");
            }
            else if (source is CasSsoProviderConfiguration expectedCas)
            {
                if (target is not CasSsoProviderConfiguration actualCas
                    || actualCas.IdentityNamespace != expectedCas.IdentityNamespace
                    || actualCas.LoginUrl != expectedCas.LoginUrl
                    || actualCas.ServiceValidateUrl != expectedCas.ServiceValidateUrl
                    || actualCas.DisplayNameAttribute != expectedCas.DisplayNameAttribute)
                    throw new InvalidOperationException("Imported CAS settings differ from the archive.");
            }
        }
    }

    private static bool NullableBytesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static void RequireVersion(JsonElement value, int version, Guid id)
    {
        if (!value.TryGetProperty("schemaVersion", out var schema)
            || schema.GetInt32() != version)
            throw new InvalidOperationException($"Record {id} has an unknown payload schema.");
    }

    private static string? OptionalString(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;
    private static Guid GuidValue(JsonElement value, string name) =>
        OptionalGuid(value, name) ?? throw new InvalidOperationException($"{name} is required.");
    private static Guid? OptionalGuid(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetGuid() : null;
    private static DateTimeOffset? OptionalDateTime(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetDateTimeOffset() : null;
    private static int? OptionalInt(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32() : null;
    private static long? OptionalLong(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt64() : null;
    private static bool? OptionalBool(JsonElement value, string name) =>
        TryProperty(value, name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean() : null;
    private static IReadOnlyList<string> OptionalStrings(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
    private static T EnumValue<T>(JsonElement value, string name) where T : struct, Enum =>
        OptionalEnum<T>(value, name)
        ?? throw new InvalidOperationException($"{name} is required.");
    private static T? OptionalEnum<T>(JsonElement value, string name) where T : struct, Enum
    {
        if (!TryProperty(value, name, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        T parsed;
        if (property.ValueKind == JsonValueKind.Number)
            parsed = (T)Enum.ToObject(typeof(T), property.GetInt32());
        else if (property.ValueKind == JsonValueKind.String
                 && Enum.TryParse<T>(property.GetString(), true, out var text))
            parsed = text;
        else
            throw new InvalidOperationException($"{name} is not a valid {typeof(T).Name}.");
        return Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"{name} has an unknown {typeof(T).Name} value.");
    }
    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property)) return true;
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return value.TryGetProperty(pascal, out property);
    }

    private sealed record Arguments(
        string ConnectionString,
        string ArchivePath,
        string KeyPath,
        string? StorageRoot)
    {
        public static Arguments Parse(string[] args)
        {
            string? Value(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }
            return new(
                Value("--connection") ?? BuildConnectionString(),
                Path.GetFullPath(Value("--archive")
                    ?? throw new InvalidOperationException("--archive is required.")),
                Path.GetFullPath(Value("--key")
                    ?? Environment.GetEnvironmentVariable("NOCTF_MIGRATION_KEY_FILE")
                    ?? throw new InvalidOperationException(
                        "--key or NOCTF_MIGRATION_KEY_FILE is required.")),
                Value("--storage-root") is { } storageRoot
                    ? Path.GetFullPath(storageRoot)
                    : null);
        }

        private static string BuildConnectionString()
        {
            var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
                ?? throw new InvalidOperationException(
                    "--connection or POSTGRES_PASSWORD is required.");
            var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "noctf";
            var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "noctf";
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "postgres";
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
            return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
        }
    }

    private sealed class NoOpLeaderboardRefreshPublisher : ILeaderboardRefreshPublisher
    {
        public Task PublishAsync(
            NoCTF.Application.Scoring.Leaderboard.ScoreboardProjection projection,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
