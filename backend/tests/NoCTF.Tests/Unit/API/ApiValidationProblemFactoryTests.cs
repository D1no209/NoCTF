using FluentValidation.Results;
using NoCTF.API.Composition;
using NoCTF.API.Localization;

namespace NoCTF.Tests.Unit.API;

public sealed class ApiValidationProblemFactoryTests
{
    [Test]
    public async Task Binding_failure_without_a_validator_code_returns_a_validation_problem()
    {
        var failure = new ValidationFailure("RuntimeId", "invalid binding") { ErrorCode = null! };

        var problem = ApiValidationProblemFactory.Create([failure], 400);

        await Assert.That(problem.Status).IsEqualTo(400);
        await Assert.That(problem.Errors.Keys).Contains("RuntimeId");
        await Assert.That(ApiValidationProblemFactory.Describe(failure).Id)
            .IsEqualTo(ApiMessageId.ValidationBinding);
    }
}
