using Microsoft.Extensions.Options;
using NoCTF.API.Pagination;

namespace NoCTF.Tests.Unit.API;

public sealed class SignedPaginationCursorTests
{
    private const string SigningKey =
        "signed-pagination-tests-use-a-stable-32-byte-signing-key";

    [Test]
    public async Task Opaque_positions_are_signed_and_filter_bound()
    {
        var cursors = new SignedKeysetCursor(Options.Create(new PaginationOptions
        {
            SigningKey = SigningKey
        }));
        var encoded = cursors.EncodeOpaque(
            "admin.platform.logs.list",
            "warning|runner",
            "20260805:1722859200000-1");

        var valid = cursors.TryDecodeOpaque(
            encoded,
            "admin.platform.logs.list",
            "warning|runner",
            out var position);
        var wrongFilter = cursors.TryDecodeOpaque(
            encoded,
            "admin.platform.logs.list",
            "error|runner",
            out _);
        var tampered = cursors.TryDecodeOpaque(
            (encoded[0] == 'A' ? 'B' : 'A') + encoded[1..],
            "admin.platform.logs.list",
            "warning|runner",
            out _);

        await Assert.That(valid).IsTrue();
        await Assert.That(position).IsEqualTo("20260805:1722859200000-1");
        await Assert.That(wrongFilter).IsFalse();
        await Assert.That(tampered).IsFalse();
    }
}
