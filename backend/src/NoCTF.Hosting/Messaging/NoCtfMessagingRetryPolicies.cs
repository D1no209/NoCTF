using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;

namespace NoCTF.Hosting.Messaging;

public static class NoCtfMessagingRetryPolicies
{
    private static readonly TimeSpan[] ScheduledRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    ];

    public static void ConfigureNoCtfInfrastructureRetries(this WolverineOptions options)
    {
        ConfigureInfrastructureRetry<TimeoutException>(options);
        ConfigureInfrastructureRetry<NpgsqlException>(options);
        options.Policies.OnException<DbUpdateConcurrencyException>().RetryTimes(5);
    }

    private static void ConfigureInfrastructureRetry<TException>(WolverineOptions options)
        where TException : Exception =>
        options.Policies.OnException<TException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
            .Then.ScheduleRetry(ScheduledRetryDelays)
            .WithFullJitter();
}
