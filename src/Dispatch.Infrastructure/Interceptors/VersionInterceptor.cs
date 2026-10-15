using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dispatch.Infrastructure.Interceptors;

/// <summary>
/// Increments <see cref="IVersioned.Version"/> on every modified versioned entity, so handlers never
/// touch it. The new value is the original (loaded) version plus one; the save-time concurrency check
/// compares against that same original value.
/// </summary>
/// <remarks>
/// A change that touches only derived data (<see cref="Shipment.SearchText"/>) does not bump the
/// version: it is not a change a client could conflict with, and bumping would hand concurrent
/// clients spurious 409s. Version itself is excluded too, so a stray hand-set value is not counted
/// as a real change.
/// </remarks>
public sealed class VersionInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> NonVersionedProperties =
    [
        nameof(Shipment.SearchText),
        nameof(IVersioned.Version),
    ];

    // EF Core does not route SaveChanges to SaveChangesAsync or the reverse, so both are overridden.
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        IncrementVersions(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        IncrementVersions(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void IncrementVersions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Entries() runs DetectChanges first, so property edits made on the entity are visible here.
        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var hasVersionedChange = entry.Properties.Any(
                p => p.IsModified && !NonVersionedProperties.Contains(p.Metadata.Name));

            if (!hasVersionedChange)
            {
                continue;
            }

            var version = entry.Property(e => e.Version);
            version.CurrentValue = version.OriginalValue + 1;
        }
    }
}
