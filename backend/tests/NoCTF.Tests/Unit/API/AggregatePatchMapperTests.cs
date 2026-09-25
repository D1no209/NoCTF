using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Teams;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.API;

public sealed class AggregatePatchMapperTests
{
    [Test]
    public async Task Self_profile_mapper_cannot_modify_credentials_or_roles()
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            UserName = "user",
            NormalizedUserName = "USER",
            Email = "user@example.test",
            PasswordHash = "protected",
            Kind = UserKind.Human,
            Role = UserRole.Administrator,
            AccountStatus = UserAccountStatus.Active,
            TokenVersion = 7,
            Description = "old"
        };

        CurrentUserProfilePatchMapper.ApplyProfileAsSelf(
            new() { Description = "new" }, user);

        await Assert.That(user.Description).IsEqualTo("new");
        await Assert.That(user.Id).IsEqualTo(id);
        await Assert.That(user.PasswordHash).IsEqualTo("protected");
        await Assert.That(user.Role).IsEqualTo(UserRole.Administrator);
        await Assert.That(user.TokenVersion).IsEqualTo(7);
    }

    [Test]
    public async Task Platform_administrator_mapper_only_changes_managed_account_fields()
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            UserName = "user",
            Email = "user@example.test",
            PasswordHash = "protected",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            TokenVersion = 9
        };

        PlatformUserPatchMapper.ApplyAsPlatformAdministrator(new()
        {
            Role = UserRoleProtocol.Organizer,
            AccountStatus = PlatformManagedUserAccountStatusProtocol.Disabled,
            EmailVerified = true
        }, user);

        await Assert.That(user.Role).IsEqualTo(UserRole.Organizer);
        await Assert.That(user.AccountStatus).IsEqualTo(UserAccountStatus.Disabled);
        await Assert.That(user.Id).IsEqualTo(id);
        await Assert.That(user.PasswordHash).IsEqualTo("protected");
        await Assert.That(user.TokenVersion).IsEqualTo(9);
    }

    [Test]
    public async Task Competition_owner_mapper_cannot_change_lifecycle_or_secret()
    {
        var id = Guid.NewGuid();
        var secret = new byte[] { 1, 2, 3 };
        var competition = new CtfCompetition
        {
            Id = id,
            OwnerId = Guid.NewGuid(),
            Status = CompetitionStatus.Running,
            FlagDerivationSecret = secret
        };
        var newOwner = Guid.NewGuid();

        CompetitionPatchMapper.ApplyPermissionsAsOwner(new()
        {
            OwnerId = newOwner,
            ManagerIds = [competition.OwnerId],
            JudgeIds = [],
            ObserverIds = []
        }, competition);

        await Assert.That(competition.OwnerId).IsEqualTo(newOwner);
        await Assert.That(competition.Id).IsEqualTo(id);
        await Assert.That(competition.Mode).IsEqualTo(GameMode.Ctf);
        await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Running);
        await Assert.That(competition.FlagDerivationSecret).IsSameReferenceAs(secret);
    }

    [Test]
    public async Task Team_captain_mapper_cannot_change_invitation_or_moderation_state()
    {
        var id = Guid.NewGuid();
        var captain = Guid.NewGuid();
        var team = new Team
        {
            Id = id,
            CompetitionId = Guid.NewGuid(),
            CaptainId = captain,
            MemberIds = [captain],
            InvitationToken = new string('x', 32),
            IsBanned = true,
            BanReason = "protected"
        };

        TeamPatchMapper.ApplyMembershipAsCaptain(new()
        {
            CaptainId = captain,
            MemberIds = [captain]
        }, team);

        await Assert.That(team.Id).IsEqualTo(id);
        await Assert.That(team.InvitationToken).IsEqualTo(new string('x', 32));
        await Assert.That(team.IsBanned).IsTrue();
        await Assert.That(team.BanReason).IsEqualTo("protected");
    }

    [Test]
    public async Task Template_content_and_competition_challenge_rules_keep_scope_identifiers()
    {
        var ownerId = Guid.NewGuid();
        var template = new CtfChallenge
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ManagerIds = [],
            Visibility = ChallengeVisibility.Private,
            Title = "old",
            Direction = "Web",
            Definition = TestConfigurations.Definition(GameMode.Ctf)
        };
        ChallengeTemplatePatchMapper.ApplyContentAsTemplateManager(new()
        {
            Mode = GameModeProtocol.Awd,
            Visibility = ChallengeVisibilityProtocol.Shared,
            Title = "new",
            Description = null,
            Direction = "Pwn",
            Definition = new ChallengeDefinitionContract
            {
                Mode = GameModeProtocol.Awd,
                Awd = new AwdChallengeDefinitionContract { FlagInjection = null },
                PatchCommand = []
            }
        }, template);

        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var instance = new CtfCompetitionChallenge
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Rules = TestConfigurations.Rules(GameMode.Ctf)
        };
        instance.Rules = CompetitionChallengeRulesContractMapper.ToDomain(
            instance.Id,
            GameMode.Ctf,
            new CompetitionChallengeRulesContract
            {
                Mode = GameModeProtocol.Ctf,
                Ctf = new CtfCompetitionChallengeRulesContract()
            });

        await Assert.That(template.OwnerId).IsEqualTo(ownerId);
        await Assert.That(instance.CompetitionId).IsEqualTo(competitionId);
        await Assert.That(instance.ChallengeId).IsEqualTo(challengeId);
        await Assert.That(instance.Rules).IsTypeOf<CtfCompetitionChallengeRules>();
    }

    [Test]
    public async Task Platform_branding_mapper_cannot_modify_credentials_or_verification_configuration()
    {
        var password = new byte[] { 7, 8, 9 };
        var capSecret = new byte[] { 10, 11, 12 };
        var turnstileSecret = new byte[] { 13, 14, 15 };
        var settings = new PlatformSettings
        {
            Id = 1,
            Name = "old",
            HumanVerificationEnabled = true,
            HumanVerificationRuntimeEnabled = false,
            HumanVerificationEvaluationEnabled = false,
            HumanVerificationProvider = HumanVerificationProvider.Cap,
            HumanVerificationCapServerUrl = "https://cap.example.test",
            HumanVerificationCapSiteKey = "cap-site-key",
            HumanVerificationCapSecretCiphertext = capSecret,
            HumanVerificationTurnstileSiteKey = "turnstile-site-key",
            HumanVerificationTurnstileSecretCiphertext = turnstileSecret,
            HumanVerificationTurnstileAllowedHostnames = ["example.test"],
            EmailSmtpPasswordCiphertext = password
        };

        PlatformSettingsPatchMapper.ApplyBrandingAsAdministrator(new()
        {
            Name = "new",
            Description = null
        }, settings);

        await Assert.That(settings.Name).IsEqualTo("new");
        await Assert.That(settings.HumanVerificationEnabled).IsTrue();
        await Assert.That(settings.HumanVerificationRuntimeEnabled).IsFalse();
        await Assert.That(settings.HumanVerificationEvaluationEnabled).IsFalse();
        await Assert.That(settings.HumanVerificationProvider)
            .IsEqualTo(HumanVerificationProvider.Cap);
        await Assert.That(settings.HumanVerificationCapServerUrl)
            .IsEqualTo("https://cap.example.test");
        await Assert.That(settings.HumanVerificationCapSiteKey)
            .IsEqualTo("cap-site-key");
        await Assert.That(settings.HumanVerificationCapSecretCiphertext)
            .IsSameReferenceAs(capSecret);
        await Assert.That(settings.HumanVerificationTurnstileSiteKey)
            .IsEqualTo("turnstile-site-key");
        await Assert.That(settings.HumanVerificationTurnstileSecretCiphertext)
            .IsSameReferenceAs(turnstileSecret);
        await Assert.That(settings.HumanVerificationTurnstileAllowedHostnames)
            .IsEquivalentTo(["example.test"]);
        await Assert.That(settings.EmailSmtpPasswordCiphertext).IsSameReferenceAs(password);
    }

    [Test]
    public async Task Section_null_clears_nullable_fields_and_arrays_are_deep_cloned()
    {
        var competition = new CtfCompetition
        {
            Description = "clear me",
            OwnerId = Guid.NewGuid(),
            ManagerIds = [Guid.NewGuid()]
        };
        CompetitionPatchMapper.ApplyMetadataAsModerator(new()
        {
            Title = "Competition",
            Description = null,
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch.AddHours(1),
            TeamRegistrationAutoApprove = true,
            AllowTeamRegistrationWhileRunning = false,
            MaxTeamMembers = 5,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            MaxActiveQuestionsPerTeam = 5,
            MaxParticipantMessagesBeforeHandlerReply = 3,
            AllowChallengeOwnersToHandleQuestions = true,
            PracticeModeEnabled = false,
            WriteUpSubmissionRequired = true,
            WriteUpSubmissionDeadlineHours = 48,
            AccessMode = CompetitionAccessModeProtocol.Public
        }, competition);

        var managerIds = new[] { Guid.NewGuid() };
        CompetitionPatchMapper.ApplyPermissionsAsOwner(new()
        {
            OwnerId = competition.OwnerId,
            ManagerIds = managerIds,
            JudgeIds = [],
            ObserverIds = []
        }, competition);
        managerIds[0] = Guid.NewGuid();

        await Assert.That(competition.Description).IsNull();
        await Assert.That(competition.WriteUpSubmissionRequired).IsTrue();
        await Assert.That(competition.WriteUpSubmissionDeadlineHours).IsEqualTo(48);
        await Assert.That(competition.ManagerIds[0]).IsNotEqualTo(managerIds[0]);
    }

    [Test]
    public async Task Self_school_and_appearance_mappers_preserve_account_security_fields()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "user",
            Email = "user@example.test",
            PasswordHash = "protected",
            Role = UserRole.Administrator,
            TokenVersion = 12,
            WallpaperFileId = Guid.NewGuid()
        };

        CurrentUserProfilePatchMapper.ApplySchoolIdentityAsSelf(new()
        {
            FullName = "School",
            StudentNumber = "S-1"
        }, user);
        CurrentUserProfilePatchMapper.ApplyAppearanceAsSelf(new()
        {
            WallpaperEnabled = true
        }, user);

        await Assert.That(user.SchoolFullName).IsEqualTo("School");
        await Assert.That(user.WallpaperEnabled).IsTrue();
        await Assert.That(user.PasswordHash).IsEqualTo("protected");
        await Assert.That(user.Role).IsEqualTo(UserRole.Administrator);
        await Assert.That(user.TokenVersion).IsEqualTo(12);
    }

    [Test]
    public async Task Platform_email_mapper_preserves_credentials_and_other_sections()
    {
        var password = new byte[] { 1, 2, 3 };
        var capSecret = new byte[] { 4, 5, 6 };
        var turnstileSecret = new byte[] { 7, 8, 9 };
        var settings = new PlatformSettings
        {
            Id = 1,
            Name = "Brand",
            HumanVerificationEnabled = true,
            HumanVerificationRuntimeEnabled = false,
            HumanVerificationEvaluationEnabled = false,
            HumanVerificationProvider = HumanVerificationProvider.Turnstile,
            HumanVerificationCapServerUrl = "https://cap.example.test",
            HumanVerificationCapSiteKey = "cap-site-key",
            HumanVerificationCapSecretCiphertext = capSecret,
            HumanVerificationTurnstileSiteKey = "turnstile-site-key",
            HumanVerificationTurnstileSecretCiphertext = turnstileSecret,
            HumanVerificationTurnstileAllowedHostnames = ["example.test"],
            EmailSmtpPasswordCiphertext = password,
            EmailVerificationEnabled = false
        };
        PlatformSettingsPatchMapper.ApplyEmailAsAdministrator(new()
        {
            Enabled = true,
            PublicBaseUrl = "https://example.test",
            TokenLifetimeMinutes = 60,
            ResendCooldownSeconds = 60,
            PasswordResetTokenLifetimeMinutes = 30,
            PasswordResetCooldownSeconds = 60,
            PasswordResetMaxRequestsPerHour = 3,
            SmtpHost = "smtp.example.test",
            SmtpPort = 587,
            SmtpSecurityMode = SmtpSecurityModeProtocol.StartTls,
            SmtpUserName = "sender",
            SmtpFromAddress = "sender@example.test",
            SmtpFromName = "Sender",
            SmtpTimeoutSeconds = 10
        }, settings);
        await Assert.That(settings.Name).IsEqualTo("Brand");
        await Assert.That(settings.HumanVerificationEnabled).IsTrue();
        await Assert.That(settings.HumanVerificationRuntimeEnabled).IsFalse();
        await Assert.That(settings.HumanVerificationEvaluationEnabled).IsFalse();
        await Assert.That(settings.HumanVerificationProvider)
            .IsEqualTo(HumanVerificationProvider.Turnstile);
        await Assert.That(settings.HumanVerificationCapServerUrl)
            .IsEqualTo("https://cap.example.test");
        await Assert.That(settings.HumanVerificationCapSiteKey)
            .IsEqualTo("cap-site-key");
        await Assert.That(settings.HumanVerificationCapSecretCiphertext)
            .IsSameReferenceAs(capSecret);
        await Assert.That(settings.HumanVerificationTurnstileSiteKey)
            .IsEqualTo("turnstile-site-key");
        await Assert.That(settings.HumanVerificationTurnstileSecretCiphertext)
            .IsSameReferenceAs(turnstileSecret);
        await Assert.That(settings.HumanVerificationTurnstileAllowedHostnames)
            .IsEquivalentTo(["example.test"]);
        await Assert.That(settings.EmailVerificationEnabled).IsTrue();
        await Assert.That(settings.EmailSmtpPasswordCiphertext).IsSameReferenceAs(password);
    }

    [Test]
    public async Task Competition_moderator_mappers_never_cross_into_permissions_or_lifecycle()
    {
        var ownerId = Guid.NewGuid();
        var secret = new byte[] { 4, 5, 6 };
        var competition = new CtfCompetition
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ManagerIds = [Guid.NewGuid()],
            Status = CompetitionStatus.Running,
            FlagDerivationSecret = secret,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf)
        };
        competition.ModeConfiguration = CompetitionModeConfigurationContractMapper.ToDomain(
            competition.Id,
            competition.Mode,
            new CompetitionModeConfigurationContract
            {
                Mode = GameModeProtocol.Ctf,
                FlagTemplate = new FlagTemplateContract("flag", "[GUID]", false),
                Ctf = new CtfCompetitionModeConfigurationContract(
                    new ScoreCurveContract(500, 100, 10, ScoreDecayModeProtocol.Quadratic, null),
                    [],
                    0)
            });
        CompetitionPatchMapper.ApplyTracksAsModerator(new()
        {
            Enabled = true,
            Tracks = []
        }, competition);
        CompetitionPatchMapper.ApplyLeaderboardAsModerator(new()
        {
            FrozenStartAt = DateTimeOffset.UnixEpoch,
            HiddenStartAt = null,
            Reason = "freeze"
        }, competition);

        await Assert.That(competition.OwnerId).IsEqualTo(ownerId);
        await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Running);
        await Assert.That(competition.FlagDerivationSecret).IsSameReferenceAs(secret);
        await Assert.That(competition.ModeConfiguration)
            .IsTypeOf<CtfCompetitionModeConfiguration>();
        await Assert.That(competition.Tracks).IsEmpty();
    }

    [Test]
    public async Task Template_permissions_and_competition_presentation_preserve_owned_content_and_links()
    {
        var template = new CtfChallenge
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Title = "Protected title",
            Direction = "Web",
            Definition = TestConfigurations.Definition(GameMode.Ctf)
        };
        var managers = new[] { Guid.NewGuid() };
        ChallengeTemplatePatchMapper.ApplyPermissionsAsTemplateOwner(new()
        {
            OwnerId = Guid.NewGuid(),
            ManagerIds = managers
        }, template);
        managers[0] = Guid.NewGuid();

        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var instance = new CtfCompetitionChallenge
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Rules = new CtfCompetitionChallengeRules { WrongSubmissionPenalty = 7 }
        };
        CompetitionChallengePatchMapper.ApplyPresentationAsCompetitionModerator(new()
        {
            CustomTitle = null,
            Order = 3,
            IsPublished = true
        }, instance);

        await Assert.That(template.Title).IsEqualTo("Protected title");
        await Assert.That(template.ManagerIds[0]).IsNotEqualTo(managers[0]);
        await Assert.That(instance.CompetitionId).IsEqualTo(competitionId);
        await Assert.That(instance.ChallengeId).IsEqualTo(challengeId);
        await Assert.That(instance.Rules!.WrongSubmissionPenalty).IsEqualTo(7);
    }

    [Test]
    public async Task Team_permission_mappers_preserve_tokens_files_and_server_audit_fields()
    {
        var captainId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var registeredAt = DateTimeOffset.UnixEpoch;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            Name = "Old",
            TrackKey = "old-track",
            CaptainId = captainId,
            MemberIds = [captainId],
            InvitationToken = new string('t', 32),
            AvatarFileId = avatarId,
            RegistrationStatus = TeamRegistrationStatus.Rejected,
            RegisteredAt = registeredAt
        };
        TeamPatchMapper.ApplyProfileAsCaptain(new() { Name = "New" }, team);
        TeamPatchMapper.ApplyRegistrationAsCaptain(new()
        {
            Status = TeamRegistrationStatusProtocol.Pending
        }, team);
        TeamPatchMapper.ApplyAdministrationAsModerator(new()
        {
            TrackKey = "new-track",
            RegistrationStatus = TeamRegistrationStatusProtocol.Approved
        }, team);
        TeamPatchMapper.ApplyBanAsJudge(new()
        {
            IsBanned = true,
            Reason = "reason",
            AnnouncePublicly = true
        }, team);
        TeamPatchMapper.ApplyUnbanAsModerator(new()
        {
            IsBanned = false,
            Reason = null
        }, team);

        await Assert.That(team.Name).IsEqualTo("New");
        await Assert.That(team.TrackKey).IsEqualTo("new-track");
        await Assert.That(team.RegistrationStatus).IsEqualTo(TeamRegistrationStatus.Approved);
        await Assert.That(team.IsBanned).IsFalse();
        await Assert.That(team.InvitationToken).IsEqualTo(new string('t', 32));
        await Assert.That(team.AvatarFileId).IsEqualTo(avatarId);
        await Assert.That(team.RegisteredAt).IsEqualTo(registeredAt);
        await Assert.That(team.BannedAt).IsNull();
        await Assert.That(team.BannedById).IsNull();
    }
}
