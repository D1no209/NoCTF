namespace NoCTF.Infrastructure;

public interface ITenantContext
{
    Guid? CompetitionId { get; }
    void SetCompetitionId(Guid? competitionId);
}

public class TenantContext : ITenantContext
{
    public Guid? CompetitionId { get; private set; }
    public void SetCompetitionId(Guid? competitionId) => CompetitionId = competitionId;
}
