# Host-triggered maintenance

Cangjie does not own a scheduler. The application, hosted service, job runner,
or external scheduler decides when maintenance runs and calls the explicit
store operation:

```csharp
var deleted = await store.DeleteExpiredAsync(stoppingToken);
```

`DeleteExpiredAsync` is safe to invoke repeatedly. It removes only expired
standalone items, preserves keyed revision history and snapshot-pinned items,
and returns the number of physical records removed.

An ASP.NET Core or worker host can use a small `BackgroundService`:

```csharp
public sealed class CangjieMaintenanceService(
    IContextStore store,
    TimeProvider timeProvider,
    ILogger<CangjieMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(1),
            timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var deleted = await store.DeleteExpiredAsync(stoppingToken);
            logger.LogInformation(
                "Cangjie expiration sweep deleted {DeletedCount} items.",
                deleted);
        }
    }
}
```

For externally scheduled execution, resolve the same `IContextStore` and call
the method once per job. Avoid overlapping sweeps in one host; SQLite
serializes writes, but duplicate jobs add contention without improving cleanup.
The `cangjie.expired.deleted` counter reports successful physical deletions.
