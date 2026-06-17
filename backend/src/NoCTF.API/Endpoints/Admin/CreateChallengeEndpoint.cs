using FastEndpoints;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CreateChallengeRequest
{
    public Guid CompetitionId { get; set; }
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

public class CreateChallengeEndpoint(ApplicationDbContext db) : Endpoint<CreateChallengeRequest, ChallengeAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CreateChallengeRequest req, CancellationToken ct)
    {
        var challenge = new Challenge
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.CompetitionId,
            Title = req.Title,
            Description = req.Description,
            TypeId = req.TypeId,
            ContainerImage = req.ContainerImage,
            ContainerMode = req.ContainerMode,
            ComposeYaml = req.ComposeYaml,
            ComposeProjectName = req.ComposeProjectName,
            FlagSecret = req.FlagSecret,
            AttachmentUrl = req.AttachmentUrl,
            PointsConfig = req.PointsConfig is not null
                ? new PointsConfig(req.PointsConfig.InitialPoints, req.PointsConfig.MinimumPoints)
                : new PointsConfig(),
            CheckerConfig = req.CheckerConfig is not null
                ? new CheckerConfig { Image = req.CheckerConfig.Image, Command = req.CheckerConfig.Command }
                : null,
            CreatedAt = DateTime.UtcNow,
        };

        db.Challenges.Add(challenge);
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
        }, 201, ct);
    }
}
