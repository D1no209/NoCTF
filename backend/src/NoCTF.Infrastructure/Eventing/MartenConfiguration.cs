using Marten;
using Marten.Events;
using JasperFx.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Eventing.Projections;
using NoCTF.Infrastructure.Eventing.ProjectionCheckpoints;
using Wolverine.Marten;

namespace NoCTF.Infrastructure.Eventing;

internal static class MartenConfiguration
{
    public static void AddNoCtfMarten(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "noctf_events";
            options.Events.DatabaseSchemaName = "noctf_events";
            options.Events.StreamIdentity = StreamIdentity.AsGuid;
            options.Schema.For<LeaderboardDocument>().Identity(document => document.Id);
            options.Schema.For<ScoringProjectionCheckpoint>().Identity(checkpoint => checkpoint.Id);
        }).IntegrateWithWolverine();
    }
}
