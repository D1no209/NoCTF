using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace NoCTF.Tests;

internal static class TestDbContextServiceProvider
{
    private static readonly IServiceProvider SharedProvider = CreateProvider();
    private static readonly IServiceProvider TransactionWarningsIgnoredProvider = CreateProvider();
    private static readonly ConditionalWeakTable<InMemoryDatabaseRoot, IServiceProvider> RootProviders = new();
    private static readonly ConditionalWeakTable<InMemoryDatabaseRoot, IServiceProvider> TransactionWarningsIgnoredRootProviders = new();

    public static DbContextOptionsBuilder UseSharedInMemoryServiceProvider(
        this DbContextOptionsBuilder builder)
        => builder.UseInternalServiceProvider(SharedProvider);

    public static DbContextOptionsBuilder<TContext> UseSharedInMemoryServiceProvider<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        bool ignoreTransactionWarnings = false)
        where TContext : DbContext
        => builder.UseInternalServiceProvider(ignoreTransactionWarnings
            ? TransactionWarningsIgnoredProvider
            : SharedProvider);

    public static DbContextOptionsBuilder<TContext> UseSharedInMemoryServiceProvider<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        InMemoryDatabaseRoot databaseRoot,
        bool ignoreTransactionWarnings = false)
        where TContext : DbContext
        => builder.UseInternalServiceProvider((ignoreTransactionWarnings
            ? TransactionWarningsIgnoredRootProviders
            : RootProviders).GetValue(databaseRoot, static _ => CreateProvider()));

    private static IServiceProvider CreateProvider()
        => new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();
}
