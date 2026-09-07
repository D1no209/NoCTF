using NoCTF.API.Endpoints.Administration.GameplayFacts;

namespace NoCTF.Tests.Unit.API;

public sealed class AdminPatchFileNameTests
{
    [Test]
    [Arguments("C:\\private\\patch.tar.gz", "patch.tar.gz")]
    [Arguments("../../patch.tar.gz", "patch.tar.gz")]
    [Arguments("patch\r\n.tar.gz", "patch.tar.gz")]
    [Arguments("原始 Patch.tar.gz", "原始 Patch.tar.gz")]
    public async Task Attachment_file_names_strip_paths_and_controls(string input, string expected) =>
        await Assert.That(DownloadAdminGameplayFactPatchEndpoint.SafeFileName(input, Guid.Empty)).IsEqualTo(expected);

    [Test]
    [Arguments("")]
    [Arguments("..")]
    [Arguments(".")]
    public async Task Empty_or_dot_names_use_a_safe_attachment_name(string input) =>
        await Assert.That(DownloadAdminGameplayFactPatchEndpoint.SafeFileName(input, Guid.Empty)).IsEqualTo($"patch-{Guid.Empty:N}.tar.gz");
}
