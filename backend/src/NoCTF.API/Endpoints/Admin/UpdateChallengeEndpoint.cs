using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class UpdateChallengeRequest
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = "ctf";
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string? FlagSecret { get; set; }
    public string? AttachmentUrl { get; set; }
    public CheckerConfigDto? CheckerConfig { get; set; }
    public PointsConfigDto? PointsConfig { get; set; }
}

public class UpdateChallengeEndpoint(ApplicationDbContext db) : Endpoint<UpdateChallengeRequest, ChallengeAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/challenges/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(UpdateChallengeRequest req, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        challenge.Title = req.Title;
        challenge.Description = req.Description;
        challenge.TypeId = req.TypeId;
        challenge.ContainerImage = req.ContainerImage;
        challenge.ContainerMode = req.ContainerMode;
        challenge.ComposeYaml = req.ComposeYaml;
        challenge.ComposeProjectName = req.ComposeProjectName;
        challenge.FlagSecret = req.FlagSecret;
        challenge.AttachmentUrl = req.AttachmentUrl;

        if (req.PointsConfig is not null)
            challenge.PointsConfig = new PointsConfig(req.PointsConfig.InitialPoints, req.PointsConfig.MinimumPoints);

        challenge.CheckerConfig = req.CheckerConfig is not null
            ? new CheckerConfig { Image = req.CheckerConfig.Image, Command = req.CheckerConfig.Command }
            : null;

        await db.SaveChangesAsync(ct);

        await SendAsync(new ChallengeAdminDto
        {
            Id = challenge.Id,
            CompetitionId = challenge.CompetitionId,
            Title = challenge.Title,
            Description = challenge.Description,
            TypeId = challenge.TypeId,
            ContainerImage = challenge.ContainerImage,
            ContainerMode = challenge.ContainerMode,
            ComposeYaml = challenge.ComposeYaml,
            ComposeProjectName = challenge.ComposeProjectName,
            AttachmentUrl = challenge.AttachmentUrl,
            CheckerConfig = challenge.CheckerConfig is null ? null : new CheckerConfigDto
            {
                Image = challenge.CheckerConfig.Image,
                Command = challenge.CheckerConfig.Command,
            },
            PointsConfig = new PointsConfigDto
            {
                InitialPoints = challenge.PointsConfig.InitialPoints,
                MinimumPoints = challenge.PointsConfig.MinimumPoints,
            },
        }, cancellation: ct);
    }
}
