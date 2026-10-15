using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dispatch.Tests;

/// <summary>
/// Test-only. Holds every save until <c>participants</c> saves have arrived, then releases them
/// together. Each request has loaded the shipment before it reaches SaveChanges, so with two
/// participants both requests are guaranteed to have read the same version: the race is decided by
/// the save-time concurrency check, never by the handler's early version comparison.
/// </summary>
internal sealed class SaveBarrierInterceptor(int participants) : SaveChangesInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public bool AllArrived => _allArrived.Task.IsCompletedSuccessfully;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _arrived) == participants)
        {
            _allArrived.TrySetResult();
        }

        // A TimeoutException surfaces as a 500, which fails the test loudly instead of hanging it.
        await _allArrived.Task.WaitAsync(Timeout, cancellationToken);
        return result;
    }
}
