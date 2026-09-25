using Microsoft.Extensions.Configuration;

namespace NoCTF.Hosting;

public enum HostRole
{
    Api,
    Worker,
    Runner
}

public sealed class HostRoles
{
    private static readonly HostRole[] AllRoles =
        [HostRole.Api, HostRole.Worker, HostRole.Runner];

    private readonly HashSet<HostRole> roles;

    private HostRoles(IEnumerable<HostRole> roles)
    {
        this.roles = roles.ToHashSet();
        if (this.roles.Count == 0)
            throw new InvalidOperationException(
                "Hosting:Roles must contain at least one of Api, Worker, or Runner.");
    }

    public IReadOnlyList<HostRole> Values =>
        AllRoles.Where(roles.Contains).ToArray();

    public bool Has(HostRole role) => roles.Contains(role);

    public static HostRoles All() => new(AllRoles);

    public static HostRoles Only(HostRole role) => new([role]);

    public static HostRoles FromConfiguration(
        IConfiguration configuration,
        HostRoles? defaults = null)
    {
        var section = configuration.GetSection("Hosting:Roles");
        var configured = section.GetChildren()
            .Select(child => child.Value)
            .ToArray();
        if (configured.Length == 0 && section.Value is not null)
            configured = section.Value.Split(',', StringSplitOptions.TrimEntries);
        if (configured.Length == 0)
            return defaults ?? All();

        var parsed = new List<HostRole>(configured.Length);
        foreach (var value in configured)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !Enum.TryParse<HostRole>(value, ignoreCase: true, out var role)
                || !Enum.IsDefined(role))
            {
                throw new InvalidOperationException(
                    $"Hosting:Roles contains invalid value '{value}'. "
                    + "Allowed values are Api, Worker, and Runner.");
            }
            parsed.Add(role);
        }
        return new HostRoles(parsed);
    }
}
