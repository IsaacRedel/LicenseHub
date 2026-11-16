using System.Net;
using LicenseHub.Api.Infrastructure;

namespace LicenseHub.IntegrationTests;

/// <summary>Pure tests of the rate limit partition key; they do not use the database.</summary>
public sealed class RateLimitPartitionTests
{
    [Theory]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234:5678::/64")]
    [InlineData("2001:db8:1234:5678:ffff:eeee:dddd:cccc", "2001:db8:1234:5678::/64")]
    [InlineData("2001:db8:1234:5679::1", "2001:db8:1234:5679::/64")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    public void PartitionKey_GroupsIpv6ByPrefixAndNormalizesMappedIpv4(string address, string expected)
    {
        Assert.Equal(expected, RateLimiting.PartitionKey(IPAddress.Parse(address)));
    }

    [Fact]
    public void PartitionKey_HandlesMissingAddress()
    {
        Assert.Equal("unknown", RateLimiting.PartitionKey(null));
    }
}
