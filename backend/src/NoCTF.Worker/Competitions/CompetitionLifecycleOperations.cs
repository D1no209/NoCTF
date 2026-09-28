using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Platform;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static async Task AdvanceCompetitionLifecycleAsync(
        AdvanceCompetitionLifecycle message,
        CompetitionLifecycleAdvancer advancer,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteCompetitionLifecycleAsync(
            message,
            advancer,
            db,
            outbox,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteCompetitionLifecycleAsync(
        AdvanceCompetitionLifecycle message,
        CompetitionLifecycleAdvancer advancer,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken)
    {
        var transitions = await advancer.ExecuteAsync(message.At, cancellationToken);
        // Capacity reconciliation remains durable maintenance work, but lifecycle is
        // the only periodic trigger owned by the Singular Agent.
        await outbox.PublishAsync(new ReconcileRunnerAssignments(message.At));
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return transitions.Count > 0
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    public static Task ApplyCompetitionVisibilityAsync(
        ApplyCompetitionVisibility message,
        CancellationToken cancellationToken) => Task.CompletedTask;

}
