namespace NoCTF.Application.Authentication;

public interface IRunnerScoringTokenIssuer
{
    string Issue(string runnerId, DateTimeOffset now);
}
