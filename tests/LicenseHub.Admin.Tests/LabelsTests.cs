using LicenseHub.Admin.Dashboard;

namespace LicenseHub.Admin.Tests;

public sealed class LabelsTests
{
    [Fact]
    public void ShortId_DistinguishesIdsCreatedInTheSameMinute()
    {
        var now = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.CreateVersion7(now)).ToList();

        var shortIds = ids.Select(id => Labels.ShortId(id)).ToHashSet();

        Assert.Equal(ids.Count, shortIds.Count);
        Assert.All(shortIds, s => Assert.Matches("^[0-9A-F]{8}$", s));
    }

    [Fact]
    public void ShortId_ShowsADashWithoutAnId()
    {
        Assert.Equal("—", Labels.ShortId(null));
    }

    [Theory]
    [InlineData("valid", "VÁLIDA")]
    [InlineData("unknownKey", "CHAVE INEXISTENTE")]
    [InlineData("productMismatch", "PRODUTO ERRADO")]
    [InlineData("revoked", "REVOGADA")]
    [InlineData("expired", "EXPIRADA")]
    [InlineData("activationLimitReached", "LIMITE DE ATIVAÇÕES")]
    public void Outcome_HasAPortugueseLabel(string outcome, string label)
    {
        Assert.Equal(label, Labels.Outcome(outcome));
    }
}
