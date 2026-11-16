using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Admins;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.UnitTests.Application;

/// <summary>In-memory stand-in for the EF Core repositories and unit of work.</summary>
internal sealed class FakeStore :
    IProductRepository,
    ILicenseRepository,
    IActivationRepository,
    IValidationLogRepository,
    IAdminUserRepository,
    IUnitOfWork
{
    private readonly List<Action> _pending = [];
    private bool _inTransaction;

    public List<AdminUser> Admins { get; } = [];

    public int AdminLockCount { get; private set; }

    public List<Product> Products { get; } = [];

    public List<License> Licenses { get; } = [];

    public List<Activation> Activations { get; } = [];

    public List<ValidationLog> ValidationLogs { get; } = [];

    public int SaveCount { get; private set; }

    public int TransactionCount { get; private set; }

    public int LockCount { get; private set; }

    /// <summary>When set, the next save throws it, simulating a database error.</summary>
    public Exception? FailNextSaveWith { get; set; }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(p => p.Id == id));

    Task<AdminUser?> IAdminUserRepository.FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Admins.SingleOrDefault(a => a.Email == email));

    Task<AdminUser?> IAdminUserRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Admins.SingleOrDefault(a => a.Id == id));

    Task<AdminUser?> IAdminUserRepository.GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_inTransaction)
        {
            throw new InvalidOperationException("GetForUpdateAsync must run inside a transaction.");
        }

        AdminLockCount++;
        return Task.FromResult(Admins.SingleOrDefault(a => a.Id == id));
    }

    void IAdminUserRepository.Add(AdminUser admin) => _pending.Add(() => Admins.Add(admin));

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(p => p.Slug == slug));

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(Products.Any(p => p.Slug == slug));

    public void Add(Product product) => _pending.Add(() => Products.Add(product));

    public Task<License?> FindByKeyHashAsync(byte[] keyHash, CancellationToken cancellationToken) =>
        Task.FromResult(Licenses.SingleOrDefault(l => l.KeyHash.AsSpan().SequenceEqual(keyHash)));

    public Task<License?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_inTransaction)
        {
            throw new InvalidOperationException("GetForUpdateAsync must run inside a transaction.");
        }

        LockCount++;
        return Task.FromResult(Licenses.SingleOrDefault(l => l.Id == id));
    }

    public void Add(License license) => _pending.Add(() => Licenses.Add(license));

    public Task<IReadOnlyList<Activation>> ListByLicenseAsync(Guid licenseId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Activation>>(Activations.Where(a => a.LicenseId == licenseId).ToList());

    public Task<Activation?> FindAsync(Guid licenseId, string instanceId, CancellationToken cancellationToken) =>
        Task.FromResult(Activations.SingleOrDefault(a => a.LicenseId == licenseId && a.InstanceId == instanceId));

    public void Add(Activation activation) => _pending.Add(() => Activations.Add(activation));

    public void Remove(Activation activation) => _pending.Add(() => Activations.Remove(activation));

    public void Add(ValidationLog log) => _pending.Add(() => ValidationLogs.Add(log));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveWith is { } failure)
        {
            FailNextSaveWith = null;
            _pending.Clear();
            throw failure;
        }

        foreach (var apply in _pending)
        {
            apply();
        }

        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        TransactionCount++;
        _inTransaction = true;
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _inTransaction = false;
        }
    }
}
