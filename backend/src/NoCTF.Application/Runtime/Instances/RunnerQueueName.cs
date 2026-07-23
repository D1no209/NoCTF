using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Application.Runtime.Instances;

public readonly record struct RunnerQueueName
{
    private const int HashPrefixLength = 22;

    public RunnerQueueName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static RunnerQueueName FromPool(string runnerPool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerPool);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(runnerPool));
        var base64Url = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')
            .ToLowerInvariant();
        return new RunnerQueueName($"noctf-runner-{base64Url[..HashPrefixLength]}");
    }

    public override string ToString() => Value;
}
