using NoCTF.API;

namespace NoCTF.Tests;

public class StorageUrlResolverTests
{
    [Theory]
    [InlineData(
        "/api/files/challenge/file.zip?expires=1&sig=old",
        "/api/files/challenge/file.zip?expires=2&sig=new")]
    [InlineData(
        "https://storage.example.test/noctf/challenge/file.zip?X-Amz-Signature=old",
        "https://storage.example.test/noctf/challenge/file.zip?X-Amz-Signature=new")]
    public void IsSameResourceUrl_IgnoresSignedQueryString(string left, string right)
    {
        Assert.True(StorageUrlResolver.IsSameResourceUrl(left, right));
    }

    [Theory]
    [InlineData(
        "https://storage.example.test/noctf/challenge/file.zip?sig=one",
        "https://attacker.example.test/noctf/challenge/file.zip?sig=two")]
    [InlineData(
        "/api/files/challenge/file.zip?sig=one",
        "/api/files/challenge/other.zip?sig=two")]
    [InlineData(
        "/api/files/challenge/file.zip?sig=one",
        "https://storage.example.test/api/files/challenge/file.zip?sig=two")]
    public void IsSameResourceUrl_RejectsDifferentResource(string left, string right)
    {
        Assert.False(StorageUrlResolver.IsSameResourceUrl(left, right));
    }
}
