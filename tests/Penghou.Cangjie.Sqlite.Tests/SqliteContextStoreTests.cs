using FluentAssertions;
using Microsoft.Data.Sqlite;
using Penghou.Cangjie.Sqlite;

namespace Penghou.Cangjie.Sqlite.Tests;

public sealed class SqliteContextStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"cangjie-tests-{Guid.NewGuid():N}");
    private readonly ManualTimeProvider clock = new(
        new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task StoreAndGet_RoundTripsContextAndGeneratesIdentity()
    {
        var store = CreateStore();

        var stored = await store.StoreAsync(new ContextItem
        {
            Scope = "repo:solo",
            Key = "decision:storage",
            Kind = ContextKind.Decision,
            Content = "Use SQLite.",
            Source = new ContextSource
            {
                Uri = "repo://docs/adr/1.md",
                Kind = "adr",
                ContentHash = "abc"
            },
            Metadata = new Dictionary<string, string>
            {
                ["model"] = "deepseek"
            },
            Tags = [" Architecture ", "architecture", "SQLite"]
        });

        stored.Id.Should().NotBe(Guid.Empty);
        stored.CreatedAt.Should().Be(clock.GetUtcNow());
        stored.Tags.Should().Equal("architecture", "sqlite");
        var loaded = await store.GetAsync(stored.Id);
        loaded.Should().BeEquivalentTo(stored);
        (await store.SearchAsync(new ContextQuery
        {
            SourceUri = "repo://docs/adr/1.md"
        })).Should().ContainSingle();
    }

    [Fact]
    public async Task Store_UpdatesAtomicallyAndPreservesOriginalCreatedAt()
    {
        var store = CreateStore();
        var original = await store.StoreAsync(Item("old searchable content") with
        {
            Tags = ["old"]
        });
        clock.Advance(TimeSpan.FromHours(1));

        var updated = await store.StoreAsync(original with
        {
            Content = "new searchable content",
            CreatedAt = clock.GetUtcNow(),
            Tags = ["new"]
        });

        updated.CreatedAt.Should().Be(original.CreatedAt);
        (await store.SearchAsync(new ContextQuery { Text = "old" }))
            .Should().BeEmpty();
        (await store.SearchAsync(new ContextQuery
        {
            Text = "new",
            Tags = ["NEW"]
        })).Should().ContainSingle();
    }

    [Theory]
    [InlineData("OrderService.PlaceOrder()")]
    [InlineData("IHandler<T>")]
    [InlineData("foo OR bar")]
    [InlineData("\"quoted\"")]
    [InlineData("a:b")]
    [InlineData("*")]
    [InlineData("-")]
    public async Task Search_TreatsArbitraryInputAsText(string text)
    {
        var store = CreateStore();
        await store.StoreAsync(Item(
            "OrderService PlaceOrder IHandler T foo OR bar quoted a b"));

        var action = async () => await store.SearchAsync(new ContextQuery
        {
            Text = text,
            SearchMode = ContextSearchMode.AnyTerm
        });

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Search_FiltersByExactScopeKindsAndAllTags()
    {
        var store = CreateStore();
        await store.StoreAsync(Item("retry compiler output") with
        {
            Scope = "repo:solo",
            Kind = ContextKind.Evidence,
            Tags = ["compiler", "failure"]
        });
        await store.StoreAsync(Item("retry compiler output") with
        {
            Scope = "repo:other",
            Kind = ContextKind.Knowledge,
            Tags = ["compiler"]
        });

        var results = await store.SearchAsync(new ContextQuery
        {
            Text = "compiler retry",
            Scope = "repo:solo",
            Kinds = [ContextKind.Evidence],
            Tags = ["compiler", "failure"]
        });

        results.Should().ContainSingle();
        results[0].Rank.Should().Be(1);
        results[0].Item.Scope.Should().Be("repo:solo");
    }

    [Fact]
    public async Task Search_WithoutTextSupportsDeterministicFilteredRetrieval()
    {
        var store = CreateStore();
        var first = await store.StoreAsync(Item("first") with
        {
            Key = "feature:todo"
        });
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await store.StoreAsync(Item("second") with
        {
            Key = "feature:todo"
        });

        var results = await store.SearchAsync(new ContextQuery
        {
            Scope = "test",
            Key = "feature:todo"
        });

        results.Select(hit => hit.Item.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task LogicalKey_PreservesAppendOnlyHistory()
    {
        var store = CreateStore();
        var first = await store.StoreAsync(Item("Version one") with
        {
            Key = "decision:storage"
        });
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await store.StoreAsync(Item("Version two") with
        {
            Key = "decision:storage"
        });
        await store.AddRelationAsync(new ContextRelation
        {
            FromId = second.Id,
            ToId = first.Id,
            Kind = ContextRelationKinds.Supersedes
        });

        (await store.GetLatestByKeyAsync("test", "decision:storage"))!
            .Id.Should().Be(second.Id);
        (await store.GetHistoryByKeyAsync("test", "decision:storage"))
            .Select(item => item.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task Expiration_HidesSearchButNotDirectGetAndCanBeCleaned()
    {
        var store = CreateStore();
        var expired = await store.StoreAsync(Item("temporary") with
        {
            ExpiresAt = clock.GetUtcNow().AddMinutes(-1)
        });

        (await store.SearchAsync(new ContextQuery { Text = "temporary" }))
            .Should().BeEmpty();
        (await store.SearchAsync(new ContextQuery
        {
            Text = "temporary",
            IncludeExpired = true
        })).Should().ContainSingle();
        (await store.GetAsync(expired.Id)).Should().NotBeNull();
        (await store.DeleteExpiredAsync()).Should().Be(1);
        (await store.GetAsync(expired.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Relations_SupportBothDirectionsAndCascadeOnDelete()
    {
        var store = CreateStore();
        var evidence = await store.StoreAsync(Item("compiler output"));
        var decision = await store.StoreAsync(Item("fix the contract") with
        {
            Kind = ContextKind.Decision
        });
        await store.AddRelationAsync(new ContextRelation
        {
            FromId = decision.Id,
            ToId = evidence.Id,
            Kind = ContextRelationKinds.DerivedFrom
        });

        (await store.GetRelationsAsync(decision.Id))
            .Should().ContainSingle();
        (await store.GetRelationsAsync(
            evidence.Id,
            ContextRelationDirection.Incoming)).Should().ContainSingle();

        await store.DeleteAsync(evidence.Id);
        (await store.GetRelationsAsync(decision.Id))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentWrites_RemainConsistent()
    {
        var store = CreateStore();

        var writes = Enumerable.Range(0, 25).Select(index =>
            store.StoreAsync(Item($"concurrent item {index}") with
            {
                Tags = ["concurrent"]
            }).AsTask());
        await Task.WhenAll(writes);

        var results = await store.SearchAsync(new ContextQuery
        {
            Text = "concurrent",
            Tags = ["concurrent"],
            Limit = 100
        });
        results.Should().HaveCount(25);
    }

    [Fact]
    public async Task Validation_RejectsInvalidInput()
    {
        var store = CreateStore();

        await FluentActions.Invoking(() => store.StoreAsync(Item(" ")).AsTask())
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => store.SearchAsync(
                new ContextQuery { Limit = 101 }).AsTask())
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => store.AddRelationAsync(
                new ContextRelation
                {
                    FromId = Guid.NewGuid(),
                    ToId = Guid.Empty,
                    Kind = ContextRelationKinds.References
                }).AsTask())
            .Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private SqliteContextStore CreateStore() => new(
        new CangjieSqliteOptions
        {
            DatabasePath = Path.Combine(directory, "context.db")
        },
        clock);

    private static ContextItem Item(string content) => new()
    {
        Scope = "test",
        Kind = ContextKind.Evidence,
        Content = content
    };

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
