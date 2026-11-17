using LicenseHub.Admin.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/nao-encontrado", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the panel with WebApplicationFactory.</summary>
public partial class Program;
