using Microsoft.EntityFrameworkCore;

namespace Dispatch.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
