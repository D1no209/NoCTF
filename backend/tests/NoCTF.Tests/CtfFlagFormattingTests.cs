using System.Reflection;
using NoCTF.Core;
using NoCTF.Plugins.CTF;

namespace NoCTF.Tests;

public class CtfFlagFormattingTests
{
    [Theory]
    [InlineData("flag", "flag{8481bb12-baa8-43e5-b878-307a5896e831}")]
    [InlineData("flag{}", "flag{8481bb12-baa8-43e5-b878-307a5896e831}")]
    [InlineData("flag{old-placeholder}", "flag{8481bb12-baa8-43e5-b878-307a5896e831}")]
    [InlineData("", "flag{8481bb12-baa8-43e5-b878-307a5896e831}")]
    public void FormatFlag_TreatsFlagHeadAndTemplateConsistently(string prefix, string expected)
    {
        var challenge = new Challenge { FlagPrefix = prefix };

        var actual = InvokeFormatFlag(challenge, "8481bb12-baa8-43e5-b878-307a5896e831");

        Assert.Equal(expected, actual);
    }

    private static string InvokeFormatFlag(Challenge challenge, string content)
    {
        var method = typeof(CtfGameMode).GetMethod(
            "FormatFlag",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, new object[] { challenge, content }));
    }
}
