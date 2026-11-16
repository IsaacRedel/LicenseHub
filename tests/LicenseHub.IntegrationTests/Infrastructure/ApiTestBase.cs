using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using LicenseHub.Api.Endpoints;
using LicenseHub.Application.Licenses;
using LicenseHub.Application.Products;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests.Infrastructure;

public abstract class ApiTestBase(MariaDbFixture database, int licensesPermitLimit = 100_000) : IAsyncLifetime
{
    private readonly ResponseSigningKey _signingKey = ResponseSigningKey.Generate();

    protected LicenseHubApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync();
        Factory = new LicenseHubApiFactory(connectionString, _signingKey.ExportPrivateKeyBase64(), licensesPermitLimit);
        Client = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        _signingKey.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates a product with a unique slug and issues one license for it, through the use cases.</summary>
    protected async Task<TestLicense> IssueLicenseAsync(int maxActivations, DateTimeOffset? expiresAt = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var slug = $"product-{Guid.NewGuid():N}"[..20];

        var product = await scope.ServiceProvider.GetRequiredService<CreateProductHandler>()
            .HandleAsync(new CreateProductCommand("Test product", slug, maxActivations), Token);
        var license = await scope.ServiceProvider.GetRequiredService<IssueLicenseHandler>()
            .HandleAsync(new IssueLicenseCommand(product.Value.Id, "buyer@example.com", ExpiresAt: expiresAt), Token);

        return new TestLicense(license.Value.LicenseId, license.Value.Key, slug);
    }

    protected async Task RevokeAsync(Guid licenseId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var license = await db.Licenses.SingleAsync(l => l.Id == licenseId, Token);
        license.Revoke(DateTimeOffset.UtcNow, "Chargeback");
        await db.SaveChangesAsync(Token);
    }

    protected async Task<T> QueryAsync<T>(Func<LicenseHubDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>());
    }

    protected static ValidateLicenseRequest NewRequest(string key, string productSlug, string instanceId) => new(
        key,
        productSlug,
        instanceId,
        Nonce: Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18)),
        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    protected Task<HttpResponseMessage> PostValidateAsync(ValidateLicenseRequest request) =>
        Client.PostAsJsonAsync("/v1/licenses/validate", request, Token);

    /// <summary>
    /// Does what a client must do: fetch the public key, verify the signature over the decoded payload bytes,
    /// and only then parse the payload.
    /// </summary>
    protected async Task<ValidationPayload> ReadVerifiedAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var signed = await response.Content.ReadFromJsonAsync<SignedResponse>(Token);
        var signingKey = await Client.GetFromJsonAsync<SigningKeyResponse>("/v1/signing-key", Token);
        Assert.NotNull(signed);
        Assert.NotNull(signingKey);
        Assert.Equal(signingKey.KeyId, signed.KeyId);

        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(signingKey.PublicKeyPem);
        var payloadBytes = Base64Url.DecodeFromChars(signed.Payload);
        var isValidSignature = publicKey.VerifyData(
            payloadBytes,
            Base64Url.DecodeFromChars(signed.Signature),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(isValidSignature, "The response signature does not verify with the published public key.");
        return ValidationPayload.Parse(payloadBytes);
    }

    protected sealed record TestLicense(Guid Id, string Key, string ProductSlug);
}
