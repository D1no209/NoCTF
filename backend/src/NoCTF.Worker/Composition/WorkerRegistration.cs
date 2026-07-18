using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Messaging;
using Wolverine;
using NoCTF.Worker.Submissions;

namespace NoCTF.Worker.Composition;

public static class WorkerRegistration
{
    public static HostApplicationBuilder AddNoCtfWorker(this HostApplicationBuilder builder)
    {
        builder.Services.AddNoCtfInfrastructure(builder.Configuration);
        builder.Services.AddHostedService<LifecycleWorker>();
        builder.Services.AddHostedService<LeaderboardRefreshWorker>();
        var connectionString = builder.Configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        builder.UseWolverine(options =>
        {
            options.ConfigureNoCtf(connectionString);
            options.Discovery.IncludeAssembly(typeof(ProcessFlagSubmissionHandler).Assembly);
        });
        return builder;
    }
}
