using System.Security.Cryptography;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.Infrastructure.Security;

public sealed class PenetrationStageFlagSecretGenerator : IPenetrationStageFlagSecretGenerator
{
    public string Generate() => $"NOCTF{{{RandomNumberGenerator.GetHexString(32, lowercase: true)}}}";
}
