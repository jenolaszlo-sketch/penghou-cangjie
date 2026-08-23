using FluentAssertions;
using Penghou.Cangjie.Sqlite;
using Penghou.Zhinu;
using Penghou.Zhinu.Sqlite;

namespace Penghou.Cangjie.Integration.Tests;

public sealed class SoloZhinuRestartProofTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"cangjie-integration-{Guid.NewGuid():N}");

    [Fact]
    public async Task ResearchToArchitectureSnapshotAndZhinuReferenceSurviveRestart()
    {
        Directory.CreateDirectory(directory);
        var contextPath = Path.Combine(directory, "context.db");
        var workflowPath = Path.Combine(directory, "workflow.db");
        Guid runId;
        await using (var engine = CreateEngine(workflowPath, contextPath))
        {
            var handle = await engine.StartHandleAsync<string, IntegrationOutcome>(
                "solo-memory-proof", "1", "Inspect streaming integrity");
            runId = handle.WorkflowRunId;
            await engine.ExecuteAsync(runId);
            var outcome = await handle.WaitAsync();
            outcome.SnapshotId.Should().NotBeEmpty();
            outcome.DecisionId.Should().NotBeEmpty();
        }

        await using var restartedEngine = CreateEngine(workflowPath, contextPath);
        var artifacts = await restartedEngine.GetArtifactsAsync(runId);
        var reference = artifacts.Should().ContainSingle().Subject;
        reference.ArtifactType.Should().Be("application/vnd.penghou.cangjie-snapshot");
        var snapshotId = Guid.Parse(reference.Location["cangjie://snapshot/".Length..]);
        var contextStore = CreateContextStore(contextPath);
        var resolution = await contextStore.ResolveSnapshotAsync(snapshotId);
        resolution.Should().NotBeNull();
        resolution!.Items.Should().ContainSingle();
        resolution.Items[0].Provenance.Producer.Should().Be("model:research-provider");
        var decisionId = Guid.Parse(reference.Metadata!["decision-id"]);
        var decision = await contextStore.GetAsync(decisionId);
        decision!.Provenance.Producer.Should().Be("model:architecture-provider");
        (await contextStore.GetRelationsAsync(decisionId)).Should().ContainSingle(
            relation => relation.ToId == resolution.Items[0].Id &&
                        relation.Kind == ContextRelationKinds.DerivedFrom);
    }

    private static WorkflowEngine CreateEngine(string workflowPath, string contextPath)
    {
        var store = new SqliteWorkflowStore(new ZhinuSqliteOptions { DatabasePath = workflowPath });
        var registry = new WorkflowRegistry().Register(
            "solo-memory-proof", "1", new SoloMemoryProofWorkflow(contextPath));
        return new WorkflowEngine(store, registry, new ZhinuOptions());
    }

    private static SqliteContextStore CreateContextStore(string path) =>
        new(new CangjieSqliteOptions { DatabasePath = path });

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class SoloMemoryProofWorkflow(string contextPath) : IWorkflow<string, IntegrationOutcome>
    {
        public async Task<IntegrationOutcome> RunAsync(
            WorkflowContext context, string input, CancellationToken cancellationToken)
        {
            var observationId = await context.StepAsync("research", input, async (request, ct) =>
            {
                var store = CreateContextStore(contextPath);
                var item = await store.StoreAsync(new ContextItem
                {
                    Scope = "solo:project",
                    Kind = ContextKinds.Evidence,
                    Content = $"Research observation for: {request}",
                    Provenance = new ContextProvenance { Producer = "model:research-provider" }
                }, cancellationToken: ct);
                return item.Id;
            }, cancellationToken: cancellationToken);

            var outcome = await context.StepAsync("architecture", observationId, async (sourceId, ct) =>
            {
                var store = CreateContextStore(contextPath);
                var hits = await store.SearchAsync(new ContextQuery
                {
                    Scope = "solo:project",
                    Kinds = [ContextKinds.Evidence],
                    Limit = 10
                }, ct);
                var snapshot = await store.StoreSnapshotAsync(new ContextSnapshot
                {
                    ItemIds = hits.Select(hit => hit.Item.Id).ToArray(),
                    QueryIdentity = "solo:architecture-input:v1",
                    Strategy = ContextSearchStrategies.Exact,
                    StrategyVersion = "sqlite-v1",
                    Purpose = "architecture decision"
                }, ct);
                var decision = await store.StoreAsync(new ContextItem
                {
                    Scope = "solo:project",
                    Kind = ContextKinds.Decision,
                    Content = "Adopt the integrity boundary.",
                    Provenance = new ContextProvenance { Producer = "model:architecture-provider" }
                }, cancellationToken: ct);
                await store.AddRelationAsync(new ContextRelation
                {
                    FromId = decision.Id,
                    ToId = sourceId,
                    Kind = ContextRelationKinds.DerivedFrom
                }, ct);
                return new IntegrationOutcome(snapshot.Id, decision.Id);
            }, cancellationToken: cancellationToken);

            await context.PublishArtifactAsync(new WorkflowArtifactDescriptor
            {
                Name = "architecture-context",
                ArtifactType = "application/vnd.penghou.cangjie-snapshot",
                ArtifactVersion = "1",
                Location = $"cangjie://snapshot/{outcome.SnapshotId:D}",
                Metadata = new Dictionary<string, string>
                {
                    ["decision-id"] = outcome.DecisionId.ToString("D")
                }
            }, cancellationToken);
            return outcome;
        }
    }

    public sealed record IntegrationOutcome(Guid SnapshotId, Guid DecisionId);
}
