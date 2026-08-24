using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Penghou.Cangjie.Sqlite;

namespace Penghou.Cangjie.Benchmarks;

[MemoryDiagnoser]
public class RetrievalBenchmarks
{
    private BenchmarkDatabase database = null!;

    [Params(10_000, 100_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        database = new BenchmarkDatabase("retrieval");
        for (var index = 0; index < ItemCount; index++)
        {
            await database.Store.StoreAsync(BenchmarkItems.Item(
                $"scope:{index % 4}",
                $"gateway architecture observation {index}"));
        }
    }

    [Benchmark(Baseline = true)]
    public ValueTask<IReadOnlyList<ContextSearchHit>> LexicalSearch() =>
        database.Store.SearchAsync(new ContextQuery
        {
            Text = "gateway architecture",
            Limit = 20
        });

    [Benchmark]
    public ValueTask<IReadOnlyList<ContextSearchHit>> LayeredScopeSearch() =>
        database.Store.SearchAsync(new ContextQuery
        {
            Text = "gateway architecture",
            Scopes = ["scope:0", "scope:1", "scope:2"],
            Limit = 20
        });

    [GlobalCleanup]
    public void Cleanup() => database.Dispose();
}

[MemoryDiagnoser]
public class SnapshotBenchmarks
{
    private BenchmarkDatabase database = null!;
    private Guid snapshotId;

    [Params(10, 100, 1_000)]
    public int SnapshotSize { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        database = new BenchmarkDatabase("snapshot");
        var ids = new List<Guid>(SnapshotSize);
        for (var index = 0; index < SnapshotSize; index++)
        {
            var item = await database.Store.StoreAsync(
                BenchmarkItems.Item("scope:snapshot", $"snapshot item {index}"));
            ids.Add(item.Id);
        }
        var snapshot = await database.Store.StoreSnapshotAsync(new ContextSnapshot
        {
            ItemIds = ids,
            QueryIdentity = "benchmark:snapshot",
            Strategy = ContextSearchStrategies.Exact,
            StrategyVersion = "benchmark-v1"
        });
        snapshotId = snapshot.Id;
    }

    [Benchmark]
    public ValueTask<ContextSnapshotResolution?> ResolveSnapshot() =>
        database.Store.ResolveSnapshotAsync(snapshotId);

    [GlobalCleanup]
    public void Cleanup() => database.Dispose();
}

[MemoryDiagnoser]
public class IngestionBenchmarks
{
    private BenchmarkDatabase database = null!;
    private int sequence;

    [GlobalSetup]
    public void Setup() => database = new BenchmarkDatabase("ingestion");

    [Benchmark(Baseline = true, OperationsPerInvoke = 100)]
    public async Task Sequential100()
    {
        var run = Interlocked.Increment(ref sequence);
        for (var index = 0; index < 100; index++)
        {
            await database.Store.StoreAsync(BenchmarkItems.Item(
                $"scope:sequential:{run}",
                $"ingested item {index}"));
        }
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public Task Concurrent100()
    {
        var run = Interlocked.Increment(ref sequence);
        return Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            database.Store.StoreAsync(BenchmarkItems.Item(
                $"scope:concurrent:{run}",
                $"ingested item {index}")).AsTask()));
    }

    [GlobalCleanup]
    public void Cleanup() => database.Dispose();
}

[MemoryDiagnoser]
public class ExpirationSweepBenchmarks
{
    private BenchmarkDatabase database = null!;

    [Params(10_000)]
    public int ExpiredItemCount { get; set; }

    [IterationSetup]
    public void Setup()
    {
        database?.Dispose();
        database = new BenchmarkDatabase("expiration");
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var index = 0; index < ExpiredItemCount; index++)
        {
            database.Store.StoreAsync(BenchmarkItems.Item(
                "scope:expired",
                $"expired item {index}") with
            {
                ExpiresAt = expiredAt
            }).AsTask().GetAwaiter().GetResult();
        }
    }

    [Benchmark]
    public ValueTask<int> DeleteExpired() => database.Store.DeleteExpiredAsync();

    [IterationCleanup]
    public void Cleanup() => database.Dispose();
}

internal sealed class BenchmarkDatabase : IDisposable
{
    private readonly string directory;

    public BenchmarkDatabase(string name)
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            $"cangjie-benchmark-{name}-{Guid.NewGuid():N}");
        Store = new SqliteContextStore(new CangjieSqliteOptions
        {
            DatabasePath = Path.Combine(directory, "context.db"),
            Pooling = false
        });
    }

    public SqliteContextStore Store { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

internal static class BenchmarkItems
{
    public static ContextItem Item(string scope, string content) => new()
    {
        Scope = scope,
        Kind = ContextKinds.Evidence,
        Content = content,
        Provenance = new ContextProvenance { Producer = "cangjie:benchmarks" }
    };
}
