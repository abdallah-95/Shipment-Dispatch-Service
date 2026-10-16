using System.Net;
using Microsoft.Data.SqlClient;

namespace Dispatch.Tests;

/// <summary>/health/live has no dependency checks; /health/ready checks SQL Server.</summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class HealthTests(TestDatabaseFixture database)
{
    [Fact]
    public async Task Live_returns_200()
    {
        await using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Ready_returns_200_when_the_database_is_reachable()
    {
        await using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_and_live_still_200_when_the_database_is_unreachable()
    {
        // Nothing listens on port 1, so the connection is refused; the short timeout bounds the test anyway.
        var unreachable = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            DataSource = "127.0.0.1,1",
            ConnectTimeout = 2,
        }.ConnectionString;

        await using var factory = new ApiFactory(unreachable);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
