using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Application.Runtime.Instances;

public readonly record struct RunnerNodeQueueName
{
    private const int HashPrefixLength = 22;

    public RunnerNodeQueueName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static RunnerNodeQueueName FromAssignment(string runnerPool, string runnerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerPool);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);

        var identity = string.Concat(runnerPool, "\0", runnerId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var base64Url = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')
            .ToLowerInvariant();
        return new RunnerNodeQueueName($"noctf-runner-node-{base64Url[..HashPrefixLength]}");
    }

    public override string ToString() => Value;
}
