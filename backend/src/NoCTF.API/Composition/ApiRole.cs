using NoCTF.API.SignalR.Publishing;
using Wolverine;

namespace NoCTF.API.Composition;

public static class ApiRole
{
    public static void ConfigureNoCtfApiMessaging(
        this WolverineOptions options,
        bool development)
    {
        if (!development)
            return;
        options.Discovery.IncludeType(typeof(LocalCompetitionEventMessageHandler));
    }
}
