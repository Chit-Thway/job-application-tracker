using JobTracker.Web.Identity;

namespace JobTracker.UnitTests;

public sealed class InvitationCodeTests
{
    [Fact]
    public void Create_ProducesDistinctUrlSafeCodes()
    {
        var first = InvitationCode.Create();
        var second = InvitationCode.Create();

        Assert.StartsWith("jit_", first, StringComparison.Ordinal);
        Assert.StartsWith("jit_", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("+", first, StringComparison.Ordinal);
        Assert.DoesNotContain("/", first, StringComparison.Ordinal);
        Assert.DoesNotContain("=", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_IsStableWithoutContainingTheReadableCode()
    {
        var code = InvitationCode.Create();

        var firstHash = InvitationCode.Hash(code);
        var secondHash = InvitationCode.Hash($"  {code}  ");

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
        Assert.DoesNotContain(code, firstHash, StringComparison.Ordinal);
    }
}
