using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloDirectionPresetConfiguration : IEntityTypeConfiguration<LiveSoloDirectionPreset>
{
    public void Configure(EntityTypeBuilder<LiveSoloDirectionPreset> builder)
    {
        builder.ToTable("live_solo_direction_presets"); builder.HasKey(x => x.Id);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloQuestionGroup>().WithMany().HasForeignKey(x => x.QuestionGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveSoloMatchDirectionConfiguration : IEntityTypeConfiguration<LiveSoloMatchDirection>
{
    public void Configure(EntityTypeBuilder<LiveSoloMatchDirection> builder)
    {
        builder.ToTable("live_solo_match_directions"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MatchId, x.Position }).IsUnique();
        builder.HasIndex(x => new { x.MatchId, x.RoundNumber }).IsUnique();
        builder.HasIndex(x => new { x.MatchId, x.QuestionGroupId }).IsUnique();
        builder.HasOne<LiveSoloMatch>().WithMany(x => x.DirectionPool).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LiveSoloQuestionGroup>().WithMany().HasForeignKey(x => x.QuestionGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveSoloRoundRosterMemberConfiguration : IEntityTypeConfiguration<LiveSoloRoundRosterMember>
{
    public void Configure(EntityTypeBuilder<LiveSoloRoundRosterMember> builder)
    {
        builder.ToTable("live_solo_round_roster_members"); builder.HasKey(x => new { x.RoundId, x.UserId });
        builder.HasOne<LiveSoloRound>().WithMany(x => x.Roster).HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveSoloRescueRequestConfiguration : IEntityTypeConfiguration<LiveSoloRescueRequest>
{
    public void Configure(EntityTypeBuilder<LiveSoloRescueRequest> builder)
    {
        builder.ToTable("live_solo_rescue_requests"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RoundId, x.TeamId, x.State });
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LiveSoloRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.JudgeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
