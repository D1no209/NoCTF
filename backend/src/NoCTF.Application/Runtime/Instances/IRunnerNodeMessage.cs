namespace NoCTF.Application.Runtime.Instances;

public interface IRunnerNodeMessage
{
    string RunnerPool { get; }
    string RunnerId { get; }
}
