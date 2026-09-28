namespace NoCTF.GameModes.Registration;

public sealed class GameModeConfigurationException : Exception
{
    public GameModeConfigurationException(string message) : base(message) { }

    public GameModeConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
