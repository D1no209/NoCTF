using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class ChallengeAdminDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = string.Empty;
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string? FlagSecret { get; set; }
    public string? AttachmentUrl { get; set; }
    public CheckerConfigDto? CheckerConfig { get; set; }
    public PointsConfigDto PointsConfig { get; set; } = new();
}

public class CheckerConfigDto
{
    public string? Image { get; set; }
    public string? Command { get; set; }
}

public class PointsConfigDto
{
    public int InitialPoints { get; set; } = 500;
    public int MinimumPoints { get; set; } = 100;
}

public class GetChallengesAdminEndpoint(ApplicationDbContext db) : Endpoint<EmptyRequest, List<ChallengeAdminDto>>
{
    public override void Configure()
    {
        Get("/api/admin/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Select(c => new ChallengeAdminDto
            {
                Id = c.Id,
                CompetitionId = c.CompetitionId,
                Title = c.Title,
                Description = c.Description,
                TypeId = c.TypeId,
                ContainerImage = c.ContainerImage,
                ContainerMode = c.ContainerMode,
                ComposeYaml = c.ComposeYaml,
                ComposeProjectName = c.ComposeProjectName,
                FlagSecret = c.FlagSecret,
                AttachmentUrl = c.AttachmentUrl,
                CheckerConfig = c.CheckerConfig == null ? null : new CheckerConfigDto
                {
                    Image = c.CheckerConfig.Image,
                    Command = c.CheckerConfig.Command,
                },
                PointsConfig = new PointsConfigDto
                {
                    InitialPoints = c.PointsConfig.InitialPoints,
                    MinimumPoints = c.PointsConfig.MinimumPoints,
                },
            })
            .ToListAsync(ct);

        await SendAsync(challenges, cancellation: ct);
    }
}
