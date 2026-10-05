using NoCTF.Application.Storage;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerManagedFileUploadRegistry : IManagedFileUploadRegistry
{
    public Task RegisterAsync(
        ManagedFileUpload file,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AbandonAsync(Guid fileId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
