using FluentAssertions;
using Microsoft.Data.Sqlite;
using Penghou.Cangjie.Sqlite;

namespace Penghou.Cangjie.Sqlite.Tests;

public sealed class GraduationContractTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"cangjie-graduation-{Guid.NewGuid():N}.db");
    private SqliteContextStore Store() => new(new CangjieSqliteOptions { DatabasePath = path, Pooling = false });
    private static ContextItem Item(string content = "evidence") => new()
    {
        Scope = "test",
        Kind = ContextKinds.Evidence,
        Content = content,
        Provenance = new ContextProvenance { Producer = "test" }
    };

    [Fact]
    public async Task BoundsRejectBeforeCommitAndAcceptBoundary()
    {
        var store = Store();
        var stored = await store.StoreAsync(Item(new string('x', 1024 * 1024)));
        (await store.GetAsync(stored.Id))!.Content.Length.Should().Be(1024 * 1024);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoreAsync(Item(new string('x', 1024 * 1024 + 1))).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoreBatchAsync(Enumerable.Range(0, 101).Select(_ => new ContextWriteRequest { Item = Item() }).ToArray()).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoreAsync(Item() with { Metadata = Enumerable.Range(0, 101).ToDictionary(i => i.ToString(), _ => "value") }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoreAsync(Item() with { Metadata = Enumerable.Range(0, 17).ToDictionary(i => i.ToString(), _ => new string('x', 4096)) }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoreSnapshotAsync(new ContextSnapshot { ItemIds = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray(), QueryIdentity = "test", Strategy = "exact", StrategyVersion = "1" }).AsTask());
        (await store.SearchAsync(new ContextQuery { Scope = "test", Limit = 100 })).Should().ContainSingle();
    }

    [Fact]
    public async Task SnapshotOwnsStateAndTraversalSurvivesConcurrentRevisionMovement()
    {
        var store = Store();
        var first = await store.StoreAsync(Item() with { Key = "concept" });
        var second = await store.StoreAsync(Item("second"));
        var ids = new List<Guid> { second.Id, first.Id };
        var metadata = new Dictionary<string, string> { ["key"] = "original" };
        var request = new ContextSnapshot { Id = Guid.NewGuid(), ItemIds = ids, Metadata = metadata, QueryIdentity = "test", Strategy = "exact", StrategyVersion = "1" };
        ids.Clear(); metadata["key"] = "changed";
        var snapshot = await store.StoreSnapshotAsync(request);
        var firstPage = (await Store().ResolveSnapshotAsync(snapshot.Id))!.Items.Take(1).ToArray();
        await Store().StoreAsync(Item("revision moved") with { Key = "concept" }, new ContextWriteOptions { ExpectedRevision = 1 });
        await Assert.ThrowsAsync<ContextStoreConflictException>(() => Store().DeleteAsync(first.Id).AsTask());
        await Assert.ThrowsAsync<ContextStoreConflictException>(() => Store().DeleteAsync(second.Id).AsTask());
        var secondPage = (await Store().ResolveSnapshotAsync(snapshot.Id))!.Items.Skip(1).ToArray();
        firstPage.Concat(secondPage).Select(item => item.Id).Should().Equal(second.Id, first.Id);
        (await Store().StoreSnapshotAsync(request)).Id.Should().Be(snapshot.Id);
        snapshot.Metadata["key"].Should().Be("original");
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)snapshot.ItemIds)[0] = Guid.NewGuid());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)snapshot.Metadata)["key"] = "changed");
    }

    [Fact]
    public async Task LegacyReadsFailOnOverflowWithoutDeletingRetainedHistory()
    {
        var store = Store();
        for (var batch = 0; batch < 10; batch++)
            await store.StoreBatchAsync(Enumerable.Range(0, 100).Select(_ => new ContextWriteRequest { Item = Item() with { Key = "long-history" } }).ToArray());
        var history = await store.GetHistoryByKeyAsync("test", "long-history");
        history.Count.Should().Be(1000);
        var snapshot = await store.StoreSnapshotAsync(new ContextSnapshot { ItemIds = history.Select(item => item.Id).ToArray(), QueryIdentity = "bounded-history", Strategy = "exact", StrategyVersion = "1" });
        await store.StoreAsync(Item() with { Key = "long-history" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetHistoryByKeyAsync("test", "long-history").AsTask());
        (await Store().GetLatestByKeyAsync("test", "long-history"))!.Revision.Should().Be(1001);
        (await Store().ResolveSnapshotAsync(snapshot.Id))!.Items.Select(item => item.Id).Should().Equal(history.Select(item => item.Id));
    }

    [Fact]
    public async Task WalOffAndCapturedOptionsSurviveReopen()
    {
        var options = new CangjieSqliteOptions { DatabasePath = path, Pooling = false, EnableWal = false };
        var store = new SqliteContextStore(options);
        options.EnableWal = true;
        options.DatabasePath = path + ".unexpected";
        var stored = await store.StoreAsync(Item());
        var snapshot = await store.StoreSnapshotAsync(new ContextSnapshot { ItemIds = [stored.Id], QueryIdentity = "wal-off", Strategy = "exact", StrategyVersion = "1" });
        (await store.CheckHealthAsync()).WalMode.Should().BeFalse();
        var peer = new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = path, Pooling = false, EnableWal = false });
        (await peer.ResolveSnapshotAsync(snapshot.Id))!.Items.Single().Id.Should().Be(stored.Id);
        File.Exists(path + ".unexpected").Should().BeFalse();
    }

    [Fact]
    public async Task HealthDoesNotBootstrapAndIncompatibleSchemaIsNotMigrated()
    {
        (await Store().CheckHealthAsync()).IsHealthy.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
        await Store().StoreAsync(Item());
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "UPDATE cangjie_schema SET version=5";
            await command.ExecuteNonQueryAsync();
        }
        (await Store().CheckHealthAsync()).IsHealthy.Should().BeFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().GetAsync(Guid.NewGuid()).AsTask());
        await using var read = new SqliteConnection($"Data Source={path};Pooling=False"); await read.OpenAsync();
        await using var version = read.CreateCommand(); version.CommandText = "SELECT version FROM cangjie_schema";
        Convert.ToInt32(await version.ExecuteScalarAsync()).Should().Be(5);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
}
