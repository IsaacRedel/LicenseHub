using System.Net;
using System.Net.Http.Json;
using Bunit;
using LicenseHub.Admin.Components.Pages;

namespace LicenseHub.Admin.Tests.Components;

public sealed class ProductsPageTests : PageTestContext
{
    private static readonly object Garage = new
    {
        id = Guid.NewGuid(),
        name = "FiveM Garage",
        slug = "fivem-garage",
        defaultMaxActivations = 2,
        createdAt = DateTimeOffset.UtcNow,
        licenseCount = 3,
    };

    [Fact]
    public void ListsTheProducts()
    {
        Api.OnJson(HttpMethod.Get, "/admin/products", new[] { Garage });

        var page = Render<Products>();

        page.WaitForAssertion(() =>
        {
            var row = page.Find("tbody tr");
            Assert.Contains("FiveM Garage", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("fivem-garage", row.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Create_ShowsTheApiErrorInPortuguese()
    {
        Api.OnJson(HttpMethod.Get, "/admin/products", Array.Empty<object>());
        Api.OnJson(HttpMethod.Post, "/admin/products", new { status = 409, code = "product.slug_taken", detail = "A product with this slug already exists." }, HttpStatusCode.Conflict);
        var page = Render<Products>();

        page.Find("#product-name").Change("FiveM Garage");
        page.Find("#product-slug").Change("fivem-garage");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Já existe um produto com este slug.", page.Find(".form .form-error").TextContent));
    }

    [Fact]
    public void Create_SendsTheFormAndRefreshesTheList()
    {
        var created = false;
        Api.On(HttpMethod.Get, "/admin/products", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(created ? new[] { Garage } : Array.Empty<object>()),
        });
        Api.On(HttpMethod.Post, "/admin/products", _ =>
        {
            created = true;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(Garage) };
        });
        var page = Render<Products>();

        page.Find("#product-name").Change("FiveM Garage");
        page.Find("#product-slug").Change("fivem-garage");
        page.Find("#product-max").Change("2");
        page.Find("form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Produto FiveM Garage cadastrado.", page.Find(".notice").TextContent, StringComparison.Ordinal);
            Assert.Single(page.FindAll("tbody tr"));
        });
        var post = Assert.Single(Api.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("/admin/products", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Create_ValidatesTheSlugBeforeCallingTheApi()
    {
        Api.OnJson(HttpMethod.Get, "/admin/products", Array.Empty<object>());
        var page = Render<Products>();

        page.Find("#product-name").Change("FiveM Garage");
        page.Find("#product-slug").Change("Not A Slug");
        page.Find("form").Submit();

        Assert.Contains("letras minúsculas", page.Find(".field__error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(Api.Requests, r => r.Method == HttpMethod.Post);
    }
}
