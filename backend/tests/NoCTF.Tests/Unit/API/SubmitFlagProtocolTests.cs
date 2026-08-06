using NoCTF.API.Endpoints.Submissions;

namespace NoCTF.Tests.Unit.API;

public sealed class SubmitFlagProtocolTests
{
    [Test]
    public async Task Single_flag_is_valid()
    {
        var result = new SubmitFlagRequestValidator().Validate(new SubmitFlagRequest
        {
            Flag = "flag{single}"
        });

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Flag_collection_is_valid()
    {
        var result = new SubmitFlagRequestValidator().Validate(new SubmitFlagRequest
        {
            Flags = ["flag{one}", "flag{two}"]
        });

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Missing_single_and_collection_is_invalid()
    {
        var result = new SubmitFlagRequestValidator().Validate(new SubmitFlagRequest());

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Supplying_single_and_collection_is_invalid()
    {
        var result = new SubmitFlagRequestValidator().Validate(new SubmitFlagRequest
        {
            Flag = "flag{single}",
            Flags = ["flag{batch}"]
        });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Null_collection_item_is_invalid()
    {
        var result = new SubmitFlagRequestValidator().Validate(new SubmitFlagRequest
        {
            Flags = ["flag{valid}", null!]
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => string.Equals(
            error.PropertyName,
            "Flags[1]",
            StringComparison.OrdinalIgnoreCase))).IsTrue();
    }
}
