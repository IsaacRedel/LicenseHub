using System.Text.RegularExpressions;
using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Products;

public sealed partial class Product
{
    private Product()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Lowercase kebab-case identifier, unique across products (e.g. "fivem-garage").</summary>
    public string Slug { get; private set; } = null!;

    public int DefaultMaxActivations { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Product Create(string name, string slug, int defaultMaxActivations, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = Guard.RequiredText(name, Limits.ProductNameLength, "product.invalid_name", "Name"),
        Slug = NormalizeSlug(slug),
        DefaultMaxActivations = Guard.ActivationLimit(defaultMaxActivations, "product.invalid_max_activations"),
        CreatedAt = now.ToUniversalTime(),
    };

    /// <summary>Trims and lowercases a slug, then checks that it is kebab-case.</summary>
    public static string NormalizeSlug(string? slug)
    {
        var normalized = Guard.RequiredText(slug, Limits.ProductSlugLength, "product.invalid_slug", "Slug")
            .ToLowerInvariant();

        if (!SlugPattern().IsMatch(normalized))
        {
            throw new DomainException(
                "product.invalid_slug",
                "Slug must contain only lowercase letters, digits and single hyphens (e.g. 'my-product').");
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
