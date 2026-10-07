using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionEntityConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(competition => competition.Id);
        builder.HasAlternateKey(competition => new { competition.Id, competition.Mode });
        builder.Property(competition => competition.Title).HasMaxLength(160);
        builder.HasIndex(competition => competition.NormalizedTitle);
        builder.HasOne(competition => competition.ModeConfiguration)
            .WithOne()
            .HasForeignKey<CompetitionModeConfiguration>(configuration => configuration.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(competition => competition.ModeConfiguration).AutoInclude();
        builder.OwnsMany(competition => competition.WebhookTargets, targets =>
        {
            targets.ToTable("competition_webhook_targets");
            targets.WithOwner().HasForeignKey(target => target.CompetitionId);
            targets.HasKey(target => target.Id);
            targets.Property(target => target.Name).HasMaxLength(160).IsRequired();
            targets.Property(target => target.EndpointUrl).HasMaxLength(2048).IsRequired();
            targets.Property(target => target.DisabledReason).HasConversion<short>();
        });
        builder.OwnsMany(competition => competition.Tracks, tracks =>
        {
            tracks.ToTable("competition_tracks");
            tracks.WithOwner().HasForeignKey(track => track.CompetitionId);
            tracks.HasKey(track => new { track.CompetitionId, track.Key });
            tracks.HasIndex(track => new { track.CompetitionId, track.Position }).IsUnique();
            tracks.HasIndex(track => track.RequiredSsoProviderId);
        });
        builder.HasMany(competition => competition.Collaborators)
            .WithOne()
            .HasForeignKey(collaborator => collaborator.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(competition => competition.Collaborators).AutoInclude();
        builder.HasDiscriminator(competition => competition.Mode)
            .HasValue<CtfCompetition>(GameMode.Ctf)
            .HasValue<AwdCompetition>(GameMode.Awd)
            .HasValue<AwdpCompetition>(GameMode.Awdp)
            .HasValue<KohCompetition>(GameMode.Koh);
        builder.Property(competition => competition.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(4);
        builder.Property(competition => competition.Status).HasConversion<short>();
        builder.Property(competition => competition.RuntimeAccessMode)
            .HasConversion<short>()
            .HasDefaultValue(NoCTF.Domain.Runtime.RuntimeAccessMode.Direct);
        builder.Property(competition => competition.TrafficCaptureEnabled)
            .HasDefaultValue(false);
        builder.Property(competition => competition.SingleWriteUpDeductionPercent).HasDefaultValue(20).ValueGeneratedNever();
        builder.Property(competition => competition.SingleWriteUpDeadlineHours).HasDefaultValue(24).ValueGeneratedNever();
        builder.Property(competition => competition.TracksEnabled)
            .ValueGeneratedNever();
        // Data Annotations cannot express these current database defaults.
        builder.Property(competition => competition.MaxActiveQuestionsPerTeam).HasDefaultValue(5);
        builder.Property(competition => competition.MaxParticipantMessagesBeforeHandlerReply).HasDefaultValue(3);
        builder.Property(competition => competition.AllowChallengeOwnersToHandleQuestions).HasDefaultValue(true);
        builder.Property(competition => competition.WriteUpSubmissionRequired).HasDefaultValue(false);
        builder.Property(competition => competition.WriteUpSubmissionDeadlineHours).HasDefaultValue(0);
        builder.HasQueryFilter(competition => competition.DeletedAt == null);
        builder.HasIndex(competition => new { competition.Status, competition.StartAt });
        builder.HasIndex(competition => new { competition.FrozenStartAt, competition.HiddenStartAt });
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(competition => competition.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(competition => competition.PosterFile).WithMany()
            .HasForeignKey(competition => competition.PosterFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompetitionModeConfigurationEntityConfiguration
    : IEntityTypeConfiguration<CompetitionModeConfiguration>
{
    public void Configure(EntityTypeBuilder<CompetitionModeConfiguration> builder)
    {
        builder.ToTable("competition_mode_configurations");
        builder.HasKey(configuration => configuration.CompetitionId);
        builder.HasDiscriminator(configuration => configuration.Mode)
            .HasValue<CtfCompetitionModeConfiguration>(GameMode.Ctf)
            .HasValue<AwdCompetitionModeConfiguration>(GameMode.Awd)
            .HasValue<AwdpCompetitionModeConfiguration>(GameMode.Awdp)
            .HasValue<KohCompetitionModeConfiguration>(GameMode.Koh);
        builder.Property(configuration => configuration.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(4);
        builder.ComplexProperty(configuration => configuration.FlagTemplate);
    }
}

internal sealed class CtfCompetitionModeConfigurationEntityConfiguration
    : IEntityTypeConfiguration<CtfCompetitionModeConfiguration>
{
    public void Configure(EntityTypeBuilder<CtfCompetitionModeConfiguration> builder)
    {
        builder.Property(configuration => configuration.ScoreSettlementMode)
            .HasDefaultValue(CtfScoreSettlementMode.DynamicRecalculation);
        builder.ComplexProperty(configuration => configuration.DefaultScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.HasMany(configuration => configuration.BloodRewards)
            .WithOne()
            .HasForeignKey(reward => reward.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(configuration => configuration.BloodRewards).AutoInclude();
    }
}

internal sealed class AwdCompetitionModeConfigurationEntityConfiguration
    : IEntityTypeConfiguration<AwdCompetitionModeConfiguration>
{
    public void Configure(EntityTypeBuilder<AwdCompetitionModeConfiguration> builder)
    {
        builder.Property(configuration => configuration.AttackRewardMode).HasConversion<short>();
    }
}

internal sealed class AwdpCompetitionModeConfigurationEntityConfiguration
    : IEntityTypeConfiguration<AwdpCompetitionModeConfiguration>
{
    public void Configure(EntityTypeBuilder<AwdpCompetitionModeConfiguration> builder)
    {
        builder.Property(configuration => configuration.EvaluationDispatchMode).HasConversion<short>();
        builder.ComplexProperty(configuration => configuration.BreakScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.ComplexProperty(configuration => configuration.FixScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
    }
}

internal sealed class CompetitionBloodRewardConfiguration
    : IEntityTypeConfiguration<CompetitionBloodReward>
{
    public void Configure(EntityTypeBuilder<CompetitionBloodReward> builder)
    {
        builder.ToTable("competition_blood_rewards");
        builder.HasKey(reward => new { reward.CompetitionId, reward.Position });
        builder.Property(reward => reward.Position).ValueGeneratedNever();
        builder.Property(reward => reward.Policy).HasConversion<short>();
        builder.Property(reward => reward.Value).HasPrecision(18, 6);
    }
}

internal sealed class CompetitionCollaboratorConfiguration
    : IEntityTypeConfiguration<CompetitionCollaborator>
{
    public void Configure(EntityTypeBuilder<CompetitionCollaborator> builder)
    {
        builder.ToTable("competition_collaborators");
        builder.HasKey(collaborator => new
        {
            collaborator.CompetitionId,
            collaborator.UserId
        });
        builder.Property(collaborator => collaborator.Role).HasConversion<short>();
        builder.HasIndex(collaborator => new
        {
            collaborator.CompetitionId,
            collaborator.Role
        });
    }
}
