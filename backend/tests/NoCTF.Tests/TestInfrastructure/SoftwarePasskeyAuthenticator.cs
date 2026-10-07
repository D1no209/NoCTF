using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoCTF.Tests;

// A disposable authenticator for protocol tests. It never accesses a user's hardware credentials.
internal sealed class SoftwarePasskeyAuthenticator : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    private string? userHandle;
    internal string Register(string optionsJson, string origin, bool verified = true, string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(optionsJson); var root = options.RootElement;
        userHandle = root.GetProperty("user").GetProperty("id").GetString();
        var challenge = challengeOverride ?? root.GetProperty("challenge").GetString();
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new { type = "webauthn.create", challenge, origin, crossOrigin = false });
        var parameters = key.ExportParameters(false);
        byte[] publicKey = [0xa5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20, ..parameters.Q.X!, 0x22, 0x58, 0x20, ..parameters.Q.Y!];
        var prefix = Data(root.GetProperty("rp").GetProperty("id").GetString()!, (byte)(verified ? 0x45 : 0x41), 0);
        var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
        byte[] data = [..prefix, ..new byte[16], ..length, ..CredentialId, ..publicKey];
        byte[] attestation = [0xa3, 0x63, ..Encoding.UTF8.GetBytes("fmt"), 0x64, ..Encoding.UTF8.GetBytes("none"),
            0x68, ..Encoding.UTF8.GetBytes("authData"), 0x58, checked((byte)data.Length), ..data, 0x67, ..Encoding.UTF8.GetBytes("attStmt"), 0xa0];
        return JsonSerializer.Serialize(new { id = Encode(CredentialId), rawId = Encode(CredentialId), type = "public-key", clientExtensionResults = new { },
            response = new { clientDataJSON = Encode(clientData), attestationObject = Encode(attestation), transports = new[] { "internal" } } });
    }
    internal string Assert(string optionsJson, string origin, uint count = 1, bool verified = true, string? challengeOverride = null, bool corruptSignature = false)
    {
        using var options = JsonDocument.Parse(optionsJson); var root = options.RootElement;
        var clientData = JsonSerializer.SerializeToUtf8Bytes(new { type = "webauthn.get", challenge = challengeOverride ?? root.GetProperty("challenge").GetString(), origin, crossOrigin = false });
        var data = Data(root.GetProperty("rpId").GetString()!, (byte)(verified ? 0x05 : 0x01), count);
        var signature = key.SignData([..data, ..SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (corruptSignature) signature[^1] ^= 1;
        return JsonSerializer.Serialize(new { id = Encode(CredentialId), rawId = Encode(CredentialId), type = "public-key", clientExtensionResults = new { },
            response = new { clientDataJSON = Encode(clientData), authenticatorData = Encode(data), signature = Encode(signature), userHandle } });
    }
    private static byte[] Data(string rp, byte flags, uint count)
    {
        var counter = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(counter, count);
        return [..SHA256.HashData(Encoding.UTF8.GetBytes(rp)), flags, ..counter];
    }
    internal static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() => key.Dispose();
}
