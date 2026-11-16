namespace LicenseHub.Domain.Common;

/// <summary>Field sizes and bounds shared by the domain and the database mapping.</summary>
public static class Limits
{
    public const int ProductNameLength = 100;
    public const int ProductSlugLength = 64;
    public const int EmailLength = 254;
    public const int InstanceIdLength = 128;
    public const int IpAddressLength = 45;
    public const int GatewayLength = 32;
    public const int ExternalEventIdLength = 128;
    public const int MaxActivations = 10_000;
}
