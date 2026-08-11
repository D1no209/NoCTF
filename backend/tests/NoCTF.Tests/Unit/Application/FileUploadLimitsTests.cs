using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class FileUploadLimitsTests
{
    [Test]
    public async Task Defaults_match_the_public_upload_contract()
    {
        await Assert.That(FileUploadLimits.Default.MaximumAvatarBytes)
            .IsEqualTo(12L * 1024 * 1024);
        await Assert.That(FileUploadLimits.Default.MaximumLogoBytes)
            .IsEqualTo(12L * 1024 * 1024);
        await Assert.That(FileUploadLimits.Default.MaximumPosterBytes)
            .IsEqualTo(12L * 1024 * 1024);
        await Assert.That(FileUploadLimits.Default.MaximumAttachmentBytes)
            .IsEqualTo(1024L * 1024 * 1024);
        await Assert.That(FileUploadLimits.Default.Validate()).IsEmpty();
    }

    [Test]
    public async Task Invalid_values_fail_startup_validation()
    {
        var limits = new FileUploadLimits(
            0,
            FileUploadLimits.MaximumConfigurableBytes + 1,
            1,
            1);

        await Assert.That(limits.Validate()).Count().IsEqualTo(2);
    }
}
