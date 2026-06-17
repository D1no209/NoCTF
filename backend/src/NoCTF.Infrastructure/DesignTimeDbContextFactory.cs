using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NoCTF.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=noctf;Username=postgres;Password=postgres");
        return new ApplicationDbContext(optionsBuilder.Options, new DummyTenantContext());
    }
}

file class DummyTenantContext : ITenantContext
{
    public Guid? CompetitionId => null;
    public void SetCompetitionId(Guid? id) { }
}
