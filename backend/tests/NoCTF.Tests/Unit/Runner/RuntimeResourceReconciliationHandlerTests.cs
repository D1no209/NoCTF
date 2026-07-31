using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RuntimeResourceReconciliationHandlerTests
{
    [Test]
    public async Task Provider_cleanup_explicitly_opts_out_of_ambient_ef_transactions()
    {
        var attributes = typeof(RuntimeResourceReconciliationHandler)
            .GetCustomAttributes(typeof(NonTransactionalAttribute), inherit: true);

        await Assert.That(attributes).HasSingleItem();
    }
}
