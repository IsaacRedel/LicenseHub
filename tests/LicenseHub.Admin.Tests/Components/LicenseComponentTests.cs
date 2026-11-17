using System.Net;
using Bunit;
using LicenseHub.Admin.Components.Licenses;
using LicenseHub.Admin.Components.Pages;

namespace LicenseHub.Admin.Tests.Components;

public sealed class LicenseComponentTests : PageTestContext
{
    private const string Key = "LH-7K3M-Q9TB-2HXA-W4RD";
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid LicenseId = Guid.NewGuid();

    public LicenseComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Api.OnJson(HttpMethod.Get, "/admin/products", new[]
        {
            new { id = ProductId, name = "FiveM Garage", slug = "fivem-garage", defaultMaxActivations = 2, createdAt = DateTimeOffset.UtcNow, licenseCount = 0 },
        });
        Api.OnJson(HttpMethod.Post, "/admin/licenses", new
        {
            id = LicenseId,
            productId = ProductId,
            productSlug = "fivem-garage",
            key = Key,
            customerEmail = "buyer@example.com",
            maxActivations = 2,
            expiresAt = (DateTimeOffset?)null,
            createdAt = DateTimeOffset.UtcNow,
        }, HttpStatusCode.Created);
    }

    [Fact]
    public void IssueDialog_ShowsTheKeyOnceAndDiscardsItWhenClosed()
    {
        var open = true;
        var dialog = Render<IssueLicenseDialog>(p => p
            .Add(d => d.Open, open)
            .Add(d => d.OnClose, () => open = false));
        IssueLicense(dialog);

        dialog.WaitForAssertion(() => Assert.Equal(Key, dialog.Find(".issued__key").TextContent));
        Assert.Equal(1, CountOccurrences(dialog.Markup, Key));
        Assert.Contains("Guarde esta chave agora. Ela não será exibida de novo.", dialog.Find(".issued__warning").TextContent, StringComparison.Ordinal);
        Assert.True(dialog.Instance.HoldsKey);

        dialog.FindAll("button").Single(b => b.TextContent.Trim() == "FECHAR").Click();

        Assert.False(open);
        Assert.False(dialog.Instance.HoldsKey);
        Assert.DoesNotContain(Key, dialog.Markup, StringComparison.Ordinal);

        // Opening again starts from an empty form, never the old key.
        dialog.Render(p => p.Add(d => d.Open, false));
        dialog.Render(p => p.Add(d => d.Open, true));
        dialog.WaitForAssertion(() => Assert.NotNull(dialog.Find("#issue-email")));
        Assert.DoesNotContain(Key, dialog.Markup, StringComparison.Ordinal);
        Assert.Equal(string.Empty, dialog.Find("#issue-email").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void IssueDialog_DiscardsTheKeyWhenClosedWithEscape()
    {
        var dialog = Render<IssueLicenseDialog>(p => p.Add(d => d.Open, true));
        IssueLicense(dialog);
        dialog.WaitForAssertion(() => Assert.True(dialog.Instance.HoldsKey));

        dialog.Find("dialog").TriggerEvent("onclose", EventArgs.Empty);

        Assert.False(dialog.Instance.HoldsKey);
        Assert.DoesNotContain(Key, dialog.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void IssueDialog_SendsOnlyTheFieldsTheApiAccepts()
    {
        var dialog = Render<IssueLicenseDialog>(p => p.Add(d => d.Open, true));

        IssueLicense(dialog);

        dialog.WaitForAssertion(() => Assert.True(dialog.Instance.HoldsKey));
        var post = Assert.Single(Api.Requests, r => r.Method == HttpMethod.Post);
        var body = Api.Bodies[post];
        Assert.Contains($"\"productId\":\"{ProductId}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"customerEmail\":\"buyer@example.com\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void LicenseDetail_RevokesAfterConfirmation()
    {
        var revoked = false;
        Api.On(HttpMethod.Get, $"/admin/licenses/{LicenseId}", _ => Json(Details(revoked ? "revoked" : "active")));
        Api.OnJson(HttpMethod.Get, $"/admin/validations?limit=50&licenseId={LicenseId}", Array.Empty<object>());
        Api.On(HttpMethod.Post, $"/admin/licenses/{LicenseId}/revoke", _ =>
        {
            revoked = true;
            return Json(Details("revoked"));
        });
        var page = Render<LicenseDetail>(p => p.Add(d => d.Id, LicenseId));
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".button-danger")));

        page.Find(".button-danger").Click();
        Assert.Contains("Revogar licença? Essa ação não pode ser desfeita.", page.Find(".modal__text").TextContent, StringComparison.Ordinal);
        page.Find("#revoke-reason").Change("Chargeback");
        page.Find(".modal form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Licença revogada.", page.Find(".notice").TextContent);
            Assert.Contains("▲ REVOGADA", page.Find(".status").TextContent, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("header .button-danger, .page-header .button-danger"));
        });
    }

    [Fact]
    public void LicensesList_ShowsStatusWithSymbolAndText()
    {
        Api.OnJson(HttpMethod.Get, "/admin/licenses?page=1&pageSize=20", new
        {
            items = new[]
            {
                Item(Guid.NewGuid(), "active"),
                Item(Guid.NewGuid(), "revoked"),
                Item(Guid.NewGuid(), "expired"),
            },
            page = 1,
            pageSize = 20,
            totalItems = 3,
            totalPages = 1,
        });

        var page = Render<Licenses>();

        page.WaitForAssertion(() =>
        {
            var statuses = page.FindAll("tbody .status").Select(s => s.TextContent.Trim()).ToList();
            Assert.Equal(["■ ATIVA", "▲ REVOGADA", "▲ EXPIRADA"], statuses);
        });
    }

    private static void IssueLicense(IRenderedComponent<IssueLicenseDialog> dialog)
    {
        dialog.WaitForAssertion(() => Assert.Equal(2, dialog.FindAll("#issue-product option").Count));
        dialog.Find("#issue-product").Change(ProductId.ToString());
        dialog.Find("#issue-email").Change("buyer@example.com");
        dialog.Find("form").Submit();
    }

    private static object Item(Guid id, string status) => new
    {
        id,
        productId = ProductId,
        productSlug = "fivem-garage",
        customerEmail = "buyer@example.com",
        status,
        maxActivations = 2,
        activeActivations = 1,
        expiresAt = (DateTimeOffset?)null,
        createdAt = DateTimeOffset.UtcNow,
        revokedAt = (DateTimeOffset?)null,
    };

    private static object Details(string status) => new
    {
        id = LicenseId,
        productId = ProductId,
        productSlug = "fivem-garage",
        customerEmail = "buyer@example.com",
        status,
        maxActivations = 2,
        activeActivations = 0,
        expiresAt = (DateTimeOffset?)null,
        createdAt = DateTimeOffset.UtcNow,
        revokedAt = status == "revoked" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
        keyHashPrefix = "1AFEC90C",
        revocation = status == "revoked"
            ? new { revokedAt = DateTimeOffset.UtcNow, reason = "Chargeback", revokedBy = new { id = Guid.NewGuid(), email = "admin@example.com" } }
            : null,
        activations = Array.Empty<object>(),
    };

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(body) };

    private static int CountOccurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
