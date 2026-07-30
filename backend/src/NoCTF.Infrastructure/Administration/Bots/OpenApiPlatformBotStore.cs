using NoCTF.Application.Administration;
using NoCTF.Application.Administration.Bots;

namespace NoCTF.Infrastructure.Administration.Bots;

public sealed class OpenApiPlatformBotStore : IPlatformBotStore
{
    public Task<(CreatePlatformBotState State, PlatformUserView? Bot)> CreateAsync(
        CreatePlatformBotCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult<(CreatePlatformBotState, PlatformUserView?)>(
            (CreatePlatformBotState.UserNameConflict, null));

    public Task<PlatformBotTokenSubject?> FindTokenSubjectAsync(
        Guid botId,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlatformBotTokenSubject?>(null);
}
