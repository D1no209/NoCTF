using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class DatabaseStartupTests
{
    [Test, Arguments("42704"), Arguments("42P01"), Arguments("42703")]
    public async Task Concurrent_migration_object_removal_rechecks_history_with_bounded_retries(string sqlState)
    {
        var attempts = 0;
        await DatabaseStartup.RunAsync(_ =>
        {
            if (++attempts < 3) throw new Npgsql.PostgresException("A peer removed the migration object", "ERROR", "ERROR", sqlState);
            return Task.CompletedTask;
        }, TimeSpan.Zero, NullLogger.Instance, CancellationToken.None);
        await Assert.That(attempts).IsEqualTo(3);
        attempts = 0;
        await Assert.That(async () => await DatabaseStartup.RunAsync(_ =>
        {
            attempts++;
            throw new Npgsql.PostgresException("Schema remains incompatible", "ERROR", "ERROR", sqlState);
        }, TimeSpan.Zero, NullLogger.Instance, CancellationToken.None)).Throws<Npgsql.PostgresException>();
        await Assert.That(attempts).IsEqualTo(7);
    }
    [Test]
    public async Task Startup_retries_transient_unavailability_then_finishes()
    {
        var attempts = 0;
        await DatabaseStartup.RunAsync(_ =>
        {
            if (++attempts < 3) throw new TimeoutException("test dependency not ready");
            return Task.CompletedTask;
        }, TimeSpan.Zero, NullLogger.Instance, CancellationToken.None);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Invalid_migrations_are_not_silently_retried()
    {
        var attempts = 0;
        try
        {
            await DatabaseStartup.RunAsync(_ => { attempts++; throw new InvalidOperationException("test migration rejected"); },
                TimeSpan.Zero, NullLogger.Instance, CancellationToken.None);
            throw new Exception("Invalid migration was ignored.");
        }
        catch (InvalidOperationException exception)
        {
            await Assert.That(exception.Message).IsEqualTo("test migration rejected");
            await Assert.That(attempts).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Startup_honors_cancellation_instead_of_retrying_forever()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        try
        {
            await DatabaseStartup.RunAsync(_ =>
            {
                attempts++;
                cancellation.Cancel();
                throw new TimeoutException("test dependency unavailable");
            }, TimeSpan.Zero, NullLogger.Instance, cancellation.Token);
            throw new Exception("Startup cancellation was ignored.");
        }
        catch (OperationCanceledException)
        {
            await Assert.That(attempts).IsEqualTo(1);
        }
    }
}
