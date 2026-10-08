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
    public static async Task CleanupFileAsync(
        CleanupFile message,
        NoCtfDbContext db,
        IStore objects,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var file = await db.Files.SingleOrDefaultAsync(item => item.Id == message.FileId, cancellationToken);
        if (file is null)
            return;
        var referenced = await db.Users.AnyAsync(item =>
                item.AvatarFileId == file.Id || item.WallpaperFileId == file.Id,
                cancellationToken)
            || await db.Teams.IgnoreQueryFilters().AnyAsync(item =>
                item.AvatarFileId == file.Id || item.WriteUpFileId == file.Id,
                cancellationToken)
            || await db.Competitions.IgnoreQueryFilters().AnyAsync(item => item.PosterFileId == file.Id, cancellationToken)
            || await db.CompetitionBadges.AnyAsync(item => item.ImageFileId == file.Id
                && item.DeletedAt == null, cancellationToken)
            || await db.PlatformSettings.AnyAsync(item => item.LogoFileId == file.Id, cancellationToken)
            || await db.Set<ChallengeAttachment>().AnyAsync(item => item.FileId == file.Id, cancellationToken)
            || await db.PatchUploads.AnyAsync(item => item.FileId == file.Id, cancellationToken)
            || await db.ChallengeWriteUpVersions.AnyAsync(item => item.FileId == file.Id, cancellationToken)
            || await db.CompetitionEvents.AnyAsync(capture =>
                capture.Kind == CompetitionEventKind.RuntimeTrafficCaptureStored
                && capture.RelatedType == EntityReferenceKind.File
                && capture.RelatedId == file.Id
                && !db.CompetitionEvents.Any(deleted =>
                    deleted.Kind == CompetitionEventKind.RuntimeTrafficCaptureDeleted
                    && deleted.ParentEventId == capture.Id),
                cancellationToken);
        if (referenced)
            return;
        await objects.DeleteObject(file.ObjectKey, cancellationToken);
        db.Files.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

}
