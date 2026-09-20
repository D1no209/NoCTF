using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class PostgresEncryptedDataProtectionKeyRepository(
    IServiceScopeFactory scopes,
    PlatformSecretProtector secrets) : IXmlRepository
{
    private const string Prefix = "noctf-sso-dp-v1:";

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        return db.DataProtectionKeys.AsNoTracking()
            .OrderBy(key => key.Id)
            .Select(key => key.Xml)
            .AsEnumerable()
            .Select(Decrypt)
            .ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var plaintext = element.ToString(SaveOptions.DisableFormatting);
        var protectedValue = secrets.Protect(
            plaintext,
            PlatformSecretPurpose.SsoDataProtectionKey);
        db.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = Prefix + Convert.ToBase64String(protectedValue)
        });
        db.SaveChanges();
    }

    private XElement Decrypt(string? value)
    {
        if (value is null)
            throw new InvalidOperationException("The stored data protection key is empty.");
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The stored data protection key format is unsupported.");
        var encoded = value[Prefix.Length..];
        var plaintext = secrets.Unprotect(
            Convert.FromBase64String(encoded),
            PlatformSecretPurpose.SsoDataProtectionKey);
        return XElement.Parse(plaintext, LoadOptions.PreserveWhitespace);
    }
}
