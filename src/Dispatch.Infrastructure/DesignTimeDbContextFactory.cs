using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dispatch.Infrastructure;

/// <summary>
/// Used by `dotnet ef` only. Generating a migration never opens a connection, so the
/// fallback below is a placeholder; set ConnectionStrings__Default to point the tooling
/// at a real server (e.g. for `dotnet ef database update`).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable($"ConnectionStrings__{DependencyInjection.ConnectionStringName}")
            ?? "Server=localhost;Database=Dispatch;Integrated Security=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
