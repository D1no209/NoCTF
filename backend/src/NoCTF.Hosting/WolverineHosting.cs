using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace NoCTF.Hosting;

public static class WolverineHosting
{
    public static void ConfigureNoCtfPersistence(
        this WolverineOptions options,
        IConfiguration configuration,
        HostRoles roles)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        options.PersistMessagesWithPostgresql(postgres, roles.PersistenceSchema);
        options.UseEntityFrameworkCoreTransactions();
    }
}
