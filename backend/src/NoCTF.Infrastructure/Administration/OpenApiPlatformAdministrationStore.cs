using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Administration;

public sealed class OpenApiPlatformAdministrationStore : IPlatformAdministrationStore
{
    public Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformUserView>>([]);
    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
    public Task<CreateBotResult> CreateBotAsync(
        string userName, UserRole role, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new CreateBotResult(CreateBotState.UserNameConflict));
    public Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId, UserRole role, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
    public Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeadLetterView>>([]);
    public Task<DeadLetterView?> FindDeadLetterAsync(
        Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult<DeadLetterView?>(null);
    public Task<bool> RequeueDeadLetterAsync(
        Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
