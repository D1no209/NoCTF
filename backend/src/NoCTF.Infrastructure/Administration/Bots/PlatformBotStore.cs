using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Bots;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration.Bots;

public sealed class PlatformBotStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher) : IPlatformBotStore
{
    public async Task<(CreatePlatformBotState State, PlatformUserView? Bot)> CreateAsync(
        CreatePlatformBotCommand command,
        CancellationToken ct)
    {
        var normalizedUserName = command.UserName.ToUpperInvariant();
        if (await db.Users.AnyAsync(
                user => user.NormalizedUserName == normalizedUserName,
                ct))
        {
            return (CreatePlatformBotState.UserNameConflict, null);
        }

        var email = $"bot-{command.BotId:N}@bots.invalid";
        var bot = new User
        {
            Id = command.BotId,
            UserName = command.UserName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Kind = UserKind.Bot,
            Role = command.Role,
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        };
        var dummyPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        bot.PasswordHash = passwordHasher.HashPassword(bot, dummyPassword);
        db.Users.Add(bot);
        try
        {
            await db.SaveChangesAsync(ct);
            return (CreatePlatformBotState.Created, Map(bot));
        }
        catch (DbUpdateException)
        {
            db.Entry(bot).State = EntityState.Detached;
            return (CreatePlatformBotState.UserNameConflict, null);
        }
    }

    public Task<PlatformBotTokenSubject?> FindTokenSubjectAsync(
        Guid botId,
        CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == botId && user.Kind == UserKind.Bot)
            .Select(user => new PlatformBotTokenSubject(
                user.Id,
                user.UserName,
                user.Role,
                user.TokenVersion))
            .SingleOrDefaultAsync(ct);

    private static PlatformUserView Map(User user) =>
        new(
            user.Id,
            user.UserName,
            user.Email,
            user.Kind,
            user.Role,
            user.TokenVersion,
            user.EmailVerifiedAt != null,
            user.CreatedAt,
            user.UpdatedAt);
}
