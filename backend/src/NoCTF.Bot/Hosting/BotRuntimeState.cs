namespace NoCTF.Bot.Hosting;

public sealed class BotRuntimeState
{
    private int noCtfAuthorized = 1;

    public bool IsNoCtfAuthorized => Volatile.Read(ref noCtfAuthorized) == 1;

    public bool MarkNoCtfUnauthorized() =>
        Interlocked.Exchange(ref noCtfAuthorized, 0) == 1;
}
