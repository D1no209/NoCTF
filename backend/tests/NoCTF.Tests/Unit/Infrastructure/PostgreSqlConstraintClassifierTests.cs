using Microsoft.EntityFrameworkCore;
using NoCTF.Persistence.PostgreSql;
using Npgsql;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class PostgreSqlConstraintClassifierTests
{
    [Test]
    [Arguments("23505", "pk_awd_capture_identities", true)]
    [Arguments("23505", "ix_awd_capture_identities_gameplay_fact_id", false)]
    [Arguments("23505", "pk_gameplay_facts", false)]
    [Arguments("23503", "pk_awd_capture_identities", false)]
    public async Task Only_the_capture_business_identity_is_a_duplicate_capture(string state, string constraint, bool expected)
    {
        var exception = new DbUpdateException("write failed", new PostgresException("constraint failure", "ERROR", "ERROR",
            state, constraintName: constraint));
        await Assert.That(new PostgreSqlConstraintClassifier().IsAwdCaptureIdentityConflict(exception)).IsEqualTo(expected);
    }
}
