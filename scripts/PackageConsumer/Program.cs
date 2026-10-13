using Microsoft.Data.Sqlite;
using Penghou.Cangjie;
using Penghou.Cangjie.Sqlite;
using Penghou.Cangjie.Testing;
using System.Diagnostics;

var path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), $"cangjie-package-{Guid.NewGuid():N}.db");
var store = new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = path, Pooling = false });
if (args.FirstOrDefault() == "append-child")
{
    for (var index = 0; index < 10; index++) await store.StoreAsync(Item("process-append") with { Key = "multi-process" });
    return;
}
if (args.FirstOrDefault() == "crash-child")
{
    var item = await store.StoreAsync(Item("committed-before-kill"));
    var snapshot = await store.StoreSnapshotAsync(new ContextSnapshot { Id = Guid.Parse("0ee30331-57b7-4823-9311-7a1dd96023bc"), ItemIds = [item.Id], QueryIdentity = "crash", Strategy = "exact", StrategyVersion = "1" });
    await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
    await connection.OpenAsync();
    await using var transaction = connection.BeginTransaction();
    await using var command = connection.CreateCommand(); command.Transaction = transaction;
    command.CommandText = "INSERT INTO context_snapshots SELECT 'fa510c92-2491-4544-8f74-d98456485333',query_identity,strategy,strategy_version,selected_at,purpose,metadata_json FROM context_snapshots LIMIT 1;";
    await command.ExecuteNonQueryAsync();
    Console.WriteLine("READY-UNCOMMITTED"); Console.Out.Flush();
    await Task.Delay(Timeout.Infinite);
    return;
}
await ContextStoreConformanceSuite.VerifyAsync(new Fixture(path));
var writerPath = path + ".writers.db";
await new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = writerPath, Pooling = false }).SearchAsync(new ContextQuery());
var writers = Enumerable.Range(0, 3).Select(_ =>
{
    var writer = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    writer.ArgumentList.Add(typeof(Program).Assembly.Location); writer.ArgumentList.Add("append-child"); writer.ArgumentList.Add(writerPath);
    return Process.Start(writer)!;
}).ToArray();
await Task.WhenAll(writers.Select(async writer => { await writer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); if (writer.ExitCode != 0) throw new Exception("Concurrent writer failed."); writer.Dispose(); }));
var history = await new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = writerPath, Pooling = false }).GetHistoryByKeyAsync("package", "multi-process");
if (!history.Select(item => item.Revision).SequenceEqual(Enumerable.Range(1, 30).Reverse())) throw new Exception("Multi-process revisions lost or duplicated.");
var childPath = path + ".crash.db";
var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("crash-child"); start.ArgumentList.Add(childPath);
using (var child = Process.Start(start)!)
{
    var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
    if (ready != "READY-UNCOMMITTED") throw new Exception("Child did not reach transaction checkpoint: " + ready);
    child.Kill(true); await child.WaitForExitAsync();
}
var reopened = new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = childPath, Pooling = false });
var recovered = await reopened.ResolveSnapshotAsync(Guid.Parse("0ee30331-57b7-4823-9311-7a1dd96023bc"));
if (recovered?.Items.Count != 1 || recovered.Items[0].Content != "committed-before-kill") throw new Exception("Committed snapshot lost after process kill.");
if (await reopened.GetSnapshotAsync(Guid.Parse("fa510c92-2491-4544-8f74-d98456485333")) is not null) throw new Exception("Uncommitted snapshot survived process kill.");
await reopened.DeleteExpiredAsync();
Console.WriteLine("SUPPORTED BY THIS PROBE: packaged conformance, multi-process revisions, committed snapshot reopen, killed SQLite transaction rollback.");
SqliteConnection.ClearAllPools();
foreach (var file in new[] { path, childPath, writerPath }) foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(file + suffix)) File.Delete(file + suffix);

static ContextItem Item(string content) => new() { Scope = "package", Kind = ContextKinds.Evidence, Content = content, Provenance = new ContextProvenance { Producer = "package-consumer" } };
sealed class Fixture(string path) : IContextStoreFixture
{
    public IContextStore Store { get; } = new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = path, Pooling = false });
    public IContextStore CreatePeerStore() => new SqliteContextStore(new CangjieSqliteOptions { DatabasePath = path, Pooling = false });
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
