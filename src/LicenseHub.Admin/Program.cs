using LicenseHub.Admin.Auth;
using LicenseHub.Admin.Components;
using LicenseHub.Admin.Dashboard;
using LicenseHub.Admin.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAdminAuthentication(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services =>
    DashboardClock.FromConfiguration(builder.Configuration, services.GetRequiredService<TimeProvider>()));

var app = builder.Build();

// First, so every response gets the headers, including errors and static files.
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/nao-encontrado", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAdminLogout();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the panel with WebApplicationFactory.</summary>
public partial class Program;
