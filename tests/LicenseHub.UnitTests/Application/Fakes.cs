using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;

namespace LicenseHub.UnitTests.Application;

/// <summary>In-memory stand-in for the EF Core repositories and unit of work.</summary>
internal sealed class FakeStore : IProductRepository, ILicenseRepository, IUnitOfWork
{
    private readonly List<Product> _pendingProducts = [];
    private readonly List<License> _pendingLicenses = [];

    public List<Product> Products { get; } = [];

    public List<License> Licenses { get; } = [];

    public int SaveCount { get; private set; }

    /// <summary>When set, the next save throws it, simulating a database error.</summary>
    public Exception? FailNextSaveWith { get; set; }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(p => p.Id == id));

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(Products.Any(p => p.Slug == slug));

    public void Add(Product product) => _pendingProducts.Add(product);

    public void Add(License license) => _pendingLicenses.Add(license);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveWith is { } failure)
        {
            FailNextSaveWith = null;
            _pendingProducts.Clear();
            _pendingLicenses.Clear();
            throw failure;
        }

        Products.AddRange(_pendingProducts);
        Licenses.AddRange(_pendingLicenses);
        _pendingProducts.Clear();
        _pendingLicenses.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}
