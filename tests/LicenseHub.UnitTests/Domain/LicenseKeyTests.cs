using System.Text.RegularExpressions;
using LicenseHub.Domain.Licenses;

namespace LicenseHub.UnitTests.Domain;

public sealed class LicenseKeyTests
{
    private static readonly Regex KeyFormat = new(
        "^LH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Generate_ReturnsKeyInExpectedFormat()
    {
        var key = LicenseKey.Generate();

        Assert.Matches(KeyFormat, key.Value);
    }

    [Fact]
    public void Generate_UsesOnlyCrockfordCharacters()
    {
        var bodyChars = Enumerable.Range(0, 500)
            .SelectMany(_ => LicenseKey.Generate().Value[LicenseKey.Prefix.Length..].Replace("-", string.Empty, StringComparison.Ordinal))
            .ToHashSet();

        Assert.All(bodyChars, c => Assert.Contains(c, LicenseKey.Alphabet));
        Assert.DoesNotContain('I', bodyChars);
        Assert.DoesNotContain('L', bodyChars);
        Assert.DoesNotContain('O', bodyChars);
        Assert.DoesNotContain('U', bodyChars);
    }

    [Fact]
    public void Generate_ProducesDistinctKeys()
    {
        const int count = 10_000;

        var keys = Enumerable.Range(0, count).Select(_ => LicenseKey.Generate().Value).ToHashSet();

        Assert.Equal(count, keys.Count);
    }

    [Fact]
    public void ComputeHash_ReturnsSha256OfTheKeyText()
    {
        Assert.True(LicenseKey.TryParse("LH-0123-4567-89AB-CDEF", out var key));

        // Reference value from: printf 'LH-0123-4567-89AB-CDEF' | sha256sum
        Assert.Equal(
            "E4E47714BD65AED2D5330FD3D9E9828F770557C373C4ADC8B4B70DFD876E5A00",
            Convert.ToHexString(key!.ComputeHash()));
    }

    [Fact]
    public void ComputeHash_IsDeterministicAndHas32Bytes()
    {
        var key = LicenseKey.Generate();

        var first = key.ComputeHash();
        var second = key.ComputeHash();

        Assert.Equal(LicenseKey.HashLength, first.Length);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("lh-0123-4567-89ab-cdef")]
    [InlineData("  LH-0123-4567-89AB-CDEF  ")]
    [InlineData("LH-0123 -4567-89AB-CDEF")]
    public void TryParse_NormalizesCaseAndWhitespace(string input)
    {
        Assert.True(LicenseKey.TryParse(input, out var key));
        Assert.Equal("LH-0123-4567-89AB-CDEF", key!.Value);
    }

    [Fact]
    public void TryParse_MapsAmbiguousCharacters()
    {
        Assert.True(LicenseKey.TryParse("LH-O1IL-0000-0000-0000", out var key));

        Assert.Equal("LH-0111-0000-0000-0000", key!.Value);
    }

    [Fact]
    public void TryParse_RoundTripsGeneratedKeyWithSameHash()
    {
        var generated = LicenseKey.Generate();

        Assert.True(LicenseKey.TryParse(generated.Value.ToLowerInvariant(), out var parsed));
        Assert.Equal(generated, parsed);
        Assert.Equal(generated.ComputeHash(), parsed!.ComputeHash());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LH-0123-4567-89AB")]
    [InlineData("LH-0123-4567-89AB-CDEF-0000")]
    [InlineData("XX-0123-4567-89AB-CDEF")]
    [InlineData("LH-0123-4567-89AB-CDEU")]
    [InlineData("LH-0123_4567-89AB-CDEF")]
    [InlineData("LH01234-4567-89AB-CDEF")]
    public void TryParse_RejectsInvalidInput(string? input)
    {
        Assert.False(LicenseKey.TryParse(input, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void ToString_DoesNotRevealTheKey()
    {
        var key = LicenseKey.Generate();

        Assert.DoesNotContain(key.Value[3..], key.ToString(), StringComparison.Ordinal);
        Assert.Equal("LH-****-****-****-****", $"{key}");
    }
}
