using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.Persistence.Durability;

namespace NoCTF.Infrastructure.Messaging;

public static class WolverineConfiguration
{
    public static void ConfigureNoCtf(this WolverineOptions options, string connectionString)
    {
        options.PersistMessagesWithPostgresql(connectionString, "noctf_messages", MessageStoreRole.Ancillary);
        options.Policies.UseDurableLocalQueues();
        options.Policies.AutoApplyTransactions();
        options.Policies.OnAnyException()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
            .Then.MoveToErrorQueue();
    }
}
