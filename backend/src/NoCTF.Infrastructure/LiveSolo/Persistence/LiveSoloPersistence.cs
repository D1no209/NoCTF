using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloMatchConfiguration : IEntityTypeConfiguration<LiveSoloMatch>
{
    public void Configure(EntityTypeBuilder<LiveSoloMatch> builder)
    {
        builder.ToTable("live_solo_matches"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompetitionId, x.Lane, x.Stage, x.Position }).IsUnique();
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.WinnerTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Slots).WithOne().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Roster).WithOne().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Rounds).WithOne().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LiveSoloRound>().WithMany().HasForeignKey(x => x.CurrentRoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.CurrentMediaSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloMatchSlotConfiguration : IEntityTypeConfiguration<LiveSoloMatchSlot>
{
    public void Configure(EntityTypeBuilder<LiveSoloMatchSlot> builder)
    {
        builder.ToTable("live_solo_match_slots"); builder.HasKey(x => new { x.MatchId, x.Side });
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.SourceMatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloRosterMemberConfiguration : IEntityTypeConfiguration<LiveSoloRosterMember>
{
    public void Configure(EntityTypeBuilder<LiveSoloRosterMember> builder)
    {
        builder.ToTable("live_solo_roster_members"); builder.HasKey(x => new { x.MatchId, x.UserId });
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloActiveTeamSlotConfiguration : IEntityTypeConfiguration<LiveSoloActiveTeamSlot>
{
    public void Configure(EntityTypeBuilder<LiveSoloActiveTeamSlot> builder)
    {
        builder.ToTable("live_solo_active_team_slots"); builder.HasKey(x => new { x.CompetitionId, x.TeamId });
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloRoundConfiguration : IEntityTypeConfiguration<LiveSoloRound>
{
    public void Configure(EntityTypeBuilder<LiveSoloRound> builder)
    {
        builder.ToTable("live_solo_rounds"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MatchId, x.Number, x.Replay }).IsUnique();
        builder.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Pauses).WithOne().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LiveSoloQuestionGroup>().WithMany().HasForeignKey(x => x.QuestionGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.WinnerTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GameplayFact>().WithMany().HasForeignKey(x => x.WinningGameplayFactId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloRoundQuestionConfiguration : IEntityTypeConfiguration<LiveSoloRoundQuestion>
{
    public void Configure(EntityTypeBuilder<LiveSoloRoundQuestion> builder)
    {
        builder.ToTable("live_solo_round_questions"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RoundId, x.Position }).IsUnique();
        builder.HasIndex(x => new { x.RoundId, x.CompetitionChallengeId }).IsUnique();
        builder.HasOne<CompetitionChallenge>().WithMany().HasForeignKey(x => x.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Runtimes).WithOne().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class LiveSoloRuntimeBindingConfiguration : IEntityTypeConfiguration<LiveSoloRuntimeBinding>
{
    public void Configure(EntityTypeBuilder<LiveSoloRuntimeBinding> builder)
    {
        builder.ToTable("live_solo_runtime_bindings"); builder.HasKey(x => new { x.RoundQuestionId, x.Side });
        builder.HasIndex(x => x.RuntimeInstanceId).IsUnique();
        builder.HasOne<RuntimeInstance>().WithMany().HasForeignKey(x => x.RuntimeInstanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloPauseIntervalConfiguration : IEntityTypeConfiguration<LiveSoloPauseInterval>
{
    public void Configure(EntityTypeBuilder<LiveSoloPauseInterval> builder)
    {
        builder.ToTable("live_solo_pause_intervals"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RoundId, x.StartedAt });
    }
}
internal sealed class LiveSoloSubmissionConfiguration : IEntityTypeConfiguration<LiveSoloSubmission>
{
    public void Configure(EntityTypeBuilder<LiveSoloSubmission> builder)
    {
        builder.ToTable("live_solo_submissions"); builder.HasKey(x => x.GameplayFactId);
        builder.HasIndex(x => new { x.RoundId, x.AdmissionSequence }).IsUnique();
        builder.HasOne<GameplayFact>().WithOne().HasForeignKey<LiveSoloSubmission>(x => x.GameplayFactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRoundQuestion>().WithMany().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloDownloadEvidenceConfiguration : IEntityTypeConfiguration<LiveSoloDownloadEvidence>
{
    public void Configure(EntityTypeBuilder<LiveSoloDownloadEvidence> builder)
    {
        builder.ToTable("live_solo_download_evidence"); builder.HasKey(x => x.GameplayFactId);
        builder.HasOne<GameplayFact>().WithOne().HasForeignKey<LiveSoloDownloadEvidence>(x => x.GameplayFactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRoundQuestion>().WithMany().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloAttachmentAssignmentConfiguration : IEntityTypeConfiguration<LiveSoloAttachmentAssignment>
{
    public void Configure(EntityTypeBuilder<LiveSoloAttachmentAssignment> builder)
    {
        builder.ToTable("live_solo_attachment_assignments"); builder.HasKey(x => new { x.RoundQuestionId, x.TeamId });
        builder.HasOne<LiveSoloRoundQuestion>().WithMany().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChallengeAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloQuestionGroupConfiguration : IEntityTypeConfiguration<LiveSoloQuestionGroup>
{
    public void Configure(EntityTypeBuilder<LiveSoloQuestionGroup> builder)
    {
        builder.ToTable("live_solo_question_groups"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompetitionId, x.Position }).IsUnique();
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.QuestionGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class LiveSoloQuestionGroupItemConfiguration : IEntityTypeConfiguration<LiveSoloQuestionGroupItem>
{
    public void Configure(EntityTypeBuilder<LiveSoloQuestionGroupItem> builder)
    {
        builder.ToTable("live_solo_question_group_items"); builder.HasKey(x => new { x.QuestionGroupId, x.Position });
        builder.HasIndex(x => new { x.QuestionGroupId, x.CompetitionChallengeId }).IsUnique();
        builder.HasOne<CompetitionChallenge>().WithMany().HasForeignKey(x => x.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloChallengeSourceConfiguration : IEntityTypeConfiguration<LiveSoloChallengeSource>
{
    public void Configure(EntityTypeBuilder<LiveSoloChallengeSource> builder)
    {
        builder.ToTable("live_solo_challenge_sources"); builder.HasKey(x => x.ChallengeId);
        builder.HasOne<Challenge>().WithOne().HasForeignKey<LiveSoloChallengeSource>(x => x.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.CanonicalChallengeId);
    }
}
internal sealed class LiveSoloQuestionExposureConfiguration : IEntityTypeConfiguration<LiveSoloQuestionExposure>
{
    public void Configure(EntityTypeBuilder<LiveSoloQuestionExposure> builder)
    {
        builder.ToTable("live_solo_question_exposures"); builder.HasKey(x => new { x.CompetitionId, x.CanonicalChallengeId });
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloCompetitionConfiguration : IEntityTypeConfiguration<LiveSoloCompetitionModeConfiguration>
{
    public void Configure(EntityTypeBuilder<LiveSoloCompetitionModeConfiguration> builder)
    {
        builder.HasMany(x => x.StageRules).WithOne().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.StageRules).AutoInclude();
    }
}
internal sealed class LiveSoloStageRuleConfiguration : IEntityTypeConfiguration<LiveSoloStageRule>
{
    public void Configure(EntityTypeBuilder<LiveSoloStageRule> builder)
    {
        builder.ToTable("live_solo_stage_rules"); builder.HasKey(x => new { x.CompetitionId, x.Lane, x.Stage });
    }
}
internal sealed class LiveSoloMediaSessionConfiguration : IEntityTypeConfiguration<LiveSoloMediaSession>
{
    public void Configure(EntityTypeBuilder<LiveSoloMediaSession> builder)
    {
        builder.ToTable("live_solo_media_sessions"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MatchId, x.Generation }).IsUnique(); builder.HasIndex(x => x.RoomIdentity).IsUnique();
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class LiveSoloMediaParticipantConfiguration : IEntityTypeConfiguration<LiveSoloMediaParticipant>
{
    public void Configure(EntityTypeBuilder<LiveSoloMediaParticipant> builder)
    {
        builder.ToTable("live_solo_media_participants"); builder.HasKey(x => new { x.MediaSessionId, x.UserId });
        builder.HasIndex(x => new { x.MediaSessionId, x.Identity }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloProgramSegmentConfiguration : IEntityTypeConfiguration<LiveSoloProgramSegment>
{
    public void Configure(EntityTypeBuilder<LiveSoloProgramSegment> builder)
    {
        builder.ToTable("live_solo_program_segments"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MediaSessionId, x.Sequence }).IsUnique(); builder.HasIndex(x => x.RemoveAfter);
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloRecordingConfiguration : IEntityTypeConfiguration<LiveSoloRecording>
{
    public void Configure(EntityTypeBuilder<LiveSoloRecording> builder)
    {
        builder.ToTable("live_solo_recordings"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.State, x.KeepUntil });
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
