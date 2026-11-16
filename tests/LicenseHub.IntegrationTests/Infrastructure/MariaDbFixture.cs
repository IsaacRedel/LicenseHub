using MySqlConnector;
using Testcontainers.MariaDb;

[assembly: AssemblyFixture(typeof(LicenseHub.IntegrationTests.Infrastructure.MariaDbFixture))]

namespace LicenseHub.IntegrationTests.Infrastructure;

/// <summary>One MariaDB container for the whole test run; each test class gets its own database.</summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    private readonly MariaDbContainer _container = new MariaDbBuilder("mariadb:11.4")
        .WithUsername("root")
        .WithPassword("integration-tests")
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and returns its connection string. Migrations run on API startup.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"lh_{Guid.NewGuid():N}"[..20];

        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4";
        await command.ExecuteNonQueryAsync();

        return new MySqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
