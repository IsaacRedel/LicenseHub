using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LicenseHub.Admin.Auth;

/// <summary>
/// Gives code running outside the circuit's DI scope (an IHttpClientFactory message handler) access to the
/// circuit's services. The documented pattern for server-side Blazor: an async-local set on every inbound circuit
/// activity.
/// </summary>
public sealed class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> CurrentServices = new();

    public IServiceProvider? Services
    {
        get => CurrentServices.Value;
        set => CurrentServices.Value = value;
    }
}

internal sealed class ServicesAccessorCircuitHandler(IServiceProvider services, CircuitServicesAccessor accessor)
    : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            accessor.Services = services;
            await next(context);
            accessor.Services = null;
        };
}
