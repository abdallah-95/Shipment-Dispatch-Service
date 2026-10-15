using Dispatch.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Tests;

/// <summary>
/// One uniquely named database for the whole test run, on the SQL Server the compose `db` service
/// provides (the custom image, so full-text search is available). Migrations are applied on start
/// and the database is dropped at the end. Tests create their own data and never read the seed.
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    public const string ServerConnectionVariable = "DISPATCH_TEST_SQLSERVER";

    private string? _serverConnectionString;
    private string? _databaseName;

    /// <summary>Connection string for this run's database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(ServerConnectionVariable)
            ?? throw new InvalidOperationException(
                $"{ServerConnectionVariable} is not set. Run the tests with: docker compose --profile tests run --rm tests");

        // "N" format (hex, no dashes) keeps the name safe to bracket-quote in DDL.
        _databaseName = $"dispatch_test_{Guid.NewGuid():N}";

        await ExecuteOnServerAsync($"CREATE DATABASE [{_databaseName}]");

        ConnectionString = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = _databaseName,
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Built through the same options method as the API, so test contexts carry the same save-time
    /// interceptors. A context without VersionInterceptor would never bump Version, and concurrency
    /// tests run through it would prove nothing.
    /// </summary>
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureDbContext(options, ConnectionString);
        return new AppDbContext(options.Options);
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null)
        {
            return;
        }

        // Pooled connections would otherwise keep the database busy and block the drop.
        SqlConnection.ClearAllPools();

        await ExecuteOnServerAsync(
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class TestDatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
    public const string Name = "TestDatabase";
}
