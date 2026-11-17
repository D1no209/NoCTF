using NoCTF.Infrastructure.Persistence;
using Npgsql;

namespace NoCTF.Persistence.PostgreSql;

public sealed class PostgreSqlConstraintClassifier : IDatabaseConstraintClassifier
{
    public bool IsCredentialIdentityConflict(Exception exception) => exception.GetBaseException() is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "ix_users_normalized_user_name" or "ix_users_normalized_email" or "ix_teams_invitation_token"
    };

    public bool IsCapabilityOutcomeIdentityConflict(Exception exception) => exception.GetBaseException() is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "pk_capability_outcomes"
    };

    public bool IsAwdCaptureIdentityConflict(Exception exception) => exception.GetBaseException() is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "pk_awd_capture_identities"
    };
}
