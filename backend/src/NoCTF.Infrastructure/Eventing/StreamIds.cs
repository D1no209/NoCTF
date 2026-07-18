namespace NoCTF.Infrastructure.Eventing;

internal static class StreamIds
{
    private static readonly Guid NamespaceId = new("4e0f253c-36ee-5eb9-bdb4-76e90299f58c");

    public static Guid Submission(Guid competitionId) =>
        CreateDeterministic($"submission:{competitionId:N}");

    public static Guid Scoring(Guid competitionId) =>
        CreateDeterministic($"scoring:{competitionId:N}");

    public static Guid InputReceipt(Guid competitionId, string idempotencyKey) =>
        CreateDeterministic($"input:{competitionId:N}:{idempotencyKey}");

    private static Guid CreateDeterministic(string name)
    {
        var namespaceBytes = NamespaceId.ToByteArray();
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);
        var hash = System.Security.Cryptography.SHA256.HashData(input);
        Span<byte> result = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(result);
        result[7] = (byte)((result[7] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);
        return new Guid(result);
    }
}
