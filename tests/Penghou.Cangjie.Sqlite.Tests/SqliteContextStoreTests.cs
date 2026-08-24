using FluentAssertions;
using Microsoft.Data.Sqlite;
using Penghou.Cangjie.Sqlite;
using Penghou.Cangjie.Testing;

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
            Kind = ContextKinds.Decision,
            Content = "Use SQLite.",
            Provenance = new ContextProvenance
            {
                Producer = "solo:research",
                ProducerVersion = "1",
                OriginatedAt = clock.GetUtcNow().AddMinutes(-5),
                Source = new ContextSource
                {
                    Uri = "repo://docs/adr/1.md",
                    Kind = "adr",
                    ContentHash = "abc"
                },
                Attributes = new Dictionary<string, string>
                {
                    ["attempt"] = "17"
                }
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
    public async Task Store_PassesReusableConformanceSuite()
    {
        await using var fixture = new SqliteConformanceFixture(
            Path.Combine(directory, "conformance.db"),
            clock);

        var report = await ContextStoreConformanceSuite.VerifyAsync(fixture);

        report.CompletedChecks.Should().Equal(
            "store-round-trip",
            "restart-persistence",
            "immutable-revisions",
            "write-conflicts",
            "scoped-idempotency",
            "relation-persistence",
            "scoped-retrieval",
            "immutable-snapshots");
    }

    [Fact]
    public async Task Restart_FreshConsumerReconstructsProviderNeutralContext()
    {
        var producerStore = CreateStore();
        var observation = await producerStore.StoreAsync(new ContextItem
        {
            Scope = "repo:solo",
            Key = "observation:persistence",
            Kind = "repository-observation",
            Content = "The context store uses SQLite FTS5.",
            Provenance = new ContextProvenance
            {
                Producer = "logical-consumer-a",
                Source = new ContextSource
                {
                    Uri = "repo://src/Penghou.Cangjie.Sqlite/SqliteContextStore.cs",
                    Kind = "repository-file",
                    ContentHash = "sha256:abc"
                }
            }
        });

        SqliteConnection.ClearAllPools();
        var consumerStore = CreateStore();
        var reconstructed = await consumerStore.GetAsync(observation.Id);

        reconstructed.Should().BeEquivalentTo(observation);
        reconstructed!.Provenance.Producer.Should().Be("logical-consumer-a");
        reconstructed.Revision.Should().Be(1);
        (await consumerStore.SearchAsync(new ContextQuery
        {
            Scope = "repo:solo",
            Text = "SQLite FTS5"
        })).Should().ContainSingle();
    }

    [Fact]
    public async Task Store_ExistingPhysicalId_IsImmutable()
    {
        var store = CreateStore();
        var original = await store.StoreAsync(Item("old searchable content") with
        {
            Tags = ["old"]
        });
        var update = async () => await store.StoreAsync(original with
        {
            Revision = 0,
            Content = "new searchable content",
            Tags = ["new"]
        });

        await update.Should().ThrowAsync<ContextStoreConflictException>();
        (await store.GetAsync(original.Id))!.Content.Should()
            .Be("old searchable content");
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
    public async Task Search_PhraseRequiresContiguousTermsWhileAllTermsDoesNot()
    {
        var store = CreateStore();
        await store.StoreAsync(Item("gateway safely references yarp"));

        var phrase = await store.SearchAsync(new ContextQuery
        {
            Text = "gateway references yarp",
            SearchMode = ContextSearchMode.Phrase
        });
        var allTerms = await store.SearchAsync(new ContextQuery
        {
            Text = "gateway references yarp",
            SearchMode = ContextSearchMode.AllTerms
        });

        phrase.Should().BeEmpty();
        allTerms.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_FiltersByExactScopeKindsAndAllTags()
    {
        var store = CreateStore();
        await store.StoreAsync(Item("retry compiler output") with
        {
            Scope = "repo:solo",
            Kind = ContextKinds.Evidence,
            Tags = ["compiler", "failure"]
        });
        await store.StoreAsync(Item("retry compiler output") with
        {
            Scope = "repo:other",
            Kind = ContextKinds.Knowledge,
            Tags = ["compiler"]
        });

        var results = await store.SearchAsync(new ContextQuery
        {
            Text = "compiler retry",
            Scope = "repo:solo",
            Kinds = [ContextKinds.Evidence],
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
        first.Revision.Should().Be(1);
        second.Revision.Should().Be(2);
        (await store.GetLatestByKeyAsync("test", "decision:storage"))!
            .Id.Should().Be(second.Id);
        (await store.GetHistoryByKeyAsync("test", "decision:storage"))
            .Select(item => item.Id).Should().Equal(second.Id, first.Id);
        (await store.GetRelationsAsync(second.Id)).Should().ContainSingle(
            relation => relation.ToId == first.Id &&
                relation.Kind == ContextRelationKinds.Supersedes);
    }

    [Fact]
    public async Task LogicalKey_ExpectedRevision_RejectsStaleWriter()
    {
        var store = CreateStore();
        await store.StoreAsync(
            Item("Version one") with { Key = "decision:storage" },
            new ContextWriteOptions { ExpectedRevision = 0 });
        await store.StoreAsync(
            Item("Version two") with { Key = "decision:storage" },
            new ContextWriteOptions { ExpectedRevision = 1 });

        var staleAppend = async () => await store.StoreAsync(
            Item("Conflicting version") with { Key = "decision:storage" },
            new ContextWriteOptions { ExpectedRevision = 1 });

        await staleAppend.Should().ThrowAsync<ContextStoreConflictException>()
            .WithMessage("*current revision is 2*");
        (await store.GetHistoryByKeyAsync("test", "decision:storage"))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task Idempotency_EquivalentRetryReturnsOriginal_ConflictIsRejected()
    {
        var store = CreateStore();
        var item = Item("compiler output");
        var options = new ContextWriteOptions
        {
            IdempotencyKey = "research:attempt-17"
        };

        var first = await store.StoreAsync(item, options);
        var retry = await store.StoreAsync(item, options);
        var conflictingRetry = async () => await store.StoreAsync(
            item with { Content = "different output" },
            options);

        retry.Should().BeEquivalentTo(first);
        await conflictingRetry.Should()
            .ThrowAsync<ContextStoreConflictException>()
            .WithMessage("*already associated with different context*");
        (await store.SearchAsync(new ContextQuery { Scope = "test" }))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task LogicalKey_ConcurrentExpectedRevision_AllowsOneWinner()
    {
        var store = CreateStore();
        await store.StoreAsync(
            Item("Version one") with { Key = "decision:storage" },
            new ContextWriteOptions { ExpectedRevision = 0 });

        async Task<Exception?> TryAppendAsync(string content)
        {
            try
            {
                await store.StoreAsync(
                    Item(content) with { Key = "decision:storage" },
                    new ContextWriteOptions { ExpectedRevision = 1 });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(
            TryAppendAsync("Version two A"),
            TryAppendAsync("Version two B"));

        outcomes.Count(outcome => outcome is null).Should().Be(1);
        outcomes.Count(outcome => outcome is ContextStoreConflictException)
            .Should().Be(1);
        (await store.GetHistoryByKeyAsync("test", "decision:storage"))
            .Select(item => item.Revision).Should().Equal(2, 1);
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
    public async Task Delete_KeyedRevision_IsRejectedToProtectHistory()
    {
        var store = CreateStore();
        var revision = await store.StoreAsync(Item("Version one") with
        {
            Key = "decision:storage"
        });

        var deletion = async () => await store.DeleteAsync(revision.Id);

        await deletion.Should().ThrowAsync<ContextStoreConflictException>()
            .WithMessage("*immutable revision history*");
        (await store.GetAsync(revision.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Relations_SupportBothDirectionsAndCascadeOnDelete()
    {
        var store = CreateStore();
        var evidence = await store.StoreAsync(Item("compiler output"));
        var decision = await store.StoreAsync(Item("fix the contract") with
        {
            Kind = ContextKinds.Decision
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
    public async Task Relations_QueryFiltersKindsLimitsAndResolvesItems()
    {
        var store = CreateStore();
        var source = await store.StoreAsync(Item("source"));
        var supported = await store.StoreAsync(Item("supported"));
        var referenced = await store.StoreAsync(Item("referenced"));
        await store.AddRelationAsync(new ContextRelation
        {
            FromId = source.Id,
            ToId = supported.Id,
            Kind = ContextRelationKinds.Supports
        });
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.AddRelationAsync(new ContextRelation
        {
            FromId = source.Id,
            ToId = referenced.Id,
            Kind = ContextRelationKinds.References
        });

        var filtered = await store.QueryRelationsAsync(
            source.Id,
            new ContextRelationQuery
            {
                Kinds = [ContextRelationKinds.References],
                Limit = 1
            });
        var related = await store.GetRelatedItemsAsync(
            source.Id,
            new ContextRelationQuery
            {
                Kinds = [ContextRelationKinds.Supports]
            });

        filtered.Should().ContainSingle()
            .Which.ToId.Should().Be(referenced.Id);
        related.Should().ContainSingle()
            .Which.Item.Id.Should().Be(supported.Id);
    }

    [Fact]
    public async Task Relations_QueryRejectsInvalidBoundsAndKinds()
    {
        var store = CreateStore();
        var item = await store.StoreAsync(Item("source"));

        var invalidLimit = () => store.QueryRelationsAsync(
            item.Id,
            new ContextRelationQuery { Limit = 0 }).AsTask();
        var blankKind = () => store.QueryRelationsAsync(
            item.Id,
            new ContextRelationQuery { Kinds = [" "] }).AsTask();

        await invalidLimit.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await blankKind.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Search_OrderedScopesPreferAndDeduplicateLogicalConcepts()
    {
        var store = CreateStore();
        var preferred = await store.StoreAsync(Item("preferred value") with
        {
            Scope = "scope:step",
            Key = "setting:theme"
        });
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.StoreAsync(Item("fallback value") with
        {
            Scope = "scope:project",
            Key = "setting:theme"
        });
        await store.StoreAsync(Item("project only") with
        {
            Scope = "scope:project",
            Key = "setting:language"
        });
        await store.StoreAsync(Item("must not leak") with
        {
            Scope = "scope:unrelated"
        });

        var results = await store.SearchAsync(new ContextQuery
        {
            Scopes = ["scope:step", "scope:project"],
            Limit = 10
        });
        var lexicalResults = await store.SearchAsync(new ContextQuery
        {
            Text = "value",
            Scopes = ["scope:step", "scope:project"],
            Limit = 10
        });

        results.Select(hit => hit.Item.Id).Should().ContainInOrder(
            preferred.Id,
            results.Single(hit => hit.Item.Content == "project only").Item.Id);
        results.Should().HaveCount(2);
        results.Should().NotContain(hit => hit.Item.Scope == "scope:unrelated");
        results.Select(hit => hit.Rank).Should().Equal(1, 2);
        lexicalResults.Should().ContainSingle()
            .Which.Item.Id.Should().Be(preferred.Id);
        lexicalResults[0].Strategy.Should().Be(ContextSearchStrategies.Lexical);
        lexicalResults[0].StrategyVersion.Should().Be("sqlite-v1");
        lexicalResults[0].Score.Should().BeNull();
    }

    [Fact]
    public async Task Search_DiagnosticsExposeCountsWithoutSensitiveValues()
    {
        var store = CreateStore();
        await store.StoreAsync(Item("private search phrase") with
        {
            Scope = "private:scope",
            Tags = ["private-tag"]
        });
        System.Diagnostics.Activity? completed = null;
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == CangjieDiagnostics.ActivitySourceName,
            Sample = static (ref System.Diagnostics.ActivityCreationOptions<
                System.Diagnostics.ActivityContext> _) =>
                    System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity => completed = activity
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        await store.SearchAsync(new ContextQuery
        {
            Text = "private search phrase",
            Scope = "private:scope",
            Tags = ["private-tag"],
            Limit = 7
        });

        completed.Should().NotBeNull();
        completed!.OperationName.Should().Be("context.search");
        completed.GetTagItem("cangjie.search.strategy")
            .Should().Be(ContextSearchStrategies.Lexical);
        completed.GetTagItem("cangjie.search.scope_count").Should().Be(1);
        completed.GetTagItem("cangjie.search.tag_count").Should().Be(1);
        completed.GetTagItem("cangjie.search.result_count").Should().Be(1);
        completed.TagObjects.Select(tag => tag.Value?.ToString())
            .Should().NotContain(["private search phrase", "private:scope", "private-tag"]);
    }

    [Fact]
    public async Task Search_DurationIsRecordedForNormalizedEmptyQuery()
    {
        var measurements = new List<double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CangjieDiagnostics.MeterName &&
                instrument.Name == CangjieDiagnostics.SearchDurationInstrumentName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        var results = await CreateStore().SearchAsync(new ContextQuery
        {
            Text = "!!!"
        });

        results.Should().BeEmpty();
        measurements.Should().ContainSingle()
            .Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Health_ReportsActualWalModeAndPropagatesCancellation()
    {
        var store = CreateStore();
        var health = await store.CheckHealthAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledProbe = () => store.CheckHealthAsync(cancellation.Token).AsTask();

        health.IsHealthy.Should().BeTrue();
        health.SchemaVersion.Should().Be(4);
        health.WalMode.Should().BeTrue();
        await cancelledProbe.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Search_RejectsAmbiguousOrInvalidScopeSets()
    {
        var store = CreateStore();

        var ambiguous = () => store.SearchAsync(new ContextQuery
        {
            Scope = "one",
            Scopes = ["two"]
        }).AsTask();
        var duplicate = () => store.SearchAsync(new ContextQuery
        {
            Scopes = ["one", "one"]
        }).AsTask();

        await ambiguous.Should().ThrowAsync<ArgumentException>();
        await duplicate.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Snapshots_RoundTripInOrderAndPinExactItems()
    {
        var store = CreateStore();
        var first = await store.StoreAsync(Item("first") with
        {
            ExpiresAt = clock.GetUtcNow().AddMinutes(1)
        });
        var second = await store.StoreAsync(Item("second"));
        var snapshot = await store.StoreSnapshotAsync(new ContextSnapshot
        {
            ItemIds = [second.Id, first.Id],
            QueryIdentity = "query:sha256:test",
            Strategy = ContextSearchStrategies.Exact,
            StrategyVersion = "test-v1",
            Purpose = "test reconstruction",
            Metadata = new Dictionary<string, string> { ["consumer"] = "tests" }
        });
        var peer = CreateStore();

        var resolution = await peer.ResolveSnapshotAsync(snapshot.Id);

        resolution.Should().NotBeNull();
        resolution!.Items.Select(item => item.Id).Should().Equal(second.Id, first.Id);
        resolution.Snapshot.Should().BeEquivalentTo(snapshot);
        await store.Invoking(value => value.DeleteAsync(first.Id).AsTask())
            .Should().ThrowAsync<ContextStoreConflictException>();
        clock.Advance(TimeSpan.FromMinutes(2));
        (await store.DeleteExpiredAsync()).Should().Be(0);
        (await peer.GetAsync(first.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Snapshots_MissingReferenceRollsBackAtomically()
    {
        var store = CreateStore();
        var existing = await store.StoreAsync(Item("existing"));
        var snapshotId = Guid.NewGuid();
        var operation = () => store.StoreSnapshotAsync(new ContextSnapshot
        {
            Id = snapshotId,
            ItemIds = [existing.Id, Guid.NewGuid()],
            QueryIdentity = "query:test",
            Strategy = ContextSearchStrategies.Exact,
            StrategyVersion = "test-v1"
        }).AsTask();

        await operation.Should().ThrowAsync<ContextStoreConflictException>();
        (await store.GetSnapshotAsync(snapshotId)).Should().BeNull();
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
        Kind = ContextKinds.Evidence,
        Content = content,
        Provenance = new ContextProvenance { Producer = "tests" }
    };

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class SqliteConformanceFixture : IContextStoreFixture
    {
        private readonly string databasePath;
        private readonly TimeProvider timeProvider;

        public SqliteConformanceFixture(
            string databasePath,
            TimeProvider timeProvider)
        {
            this.databasePath = databasePath;
            this.timeProvider = timeProvider;
            Store = CreatePeerStore();
        }

        public IContextStore Store { get; }

        public IContextStore CreatePeerStore() => new SqliteContextStore(
            new CangjieSqliteOptions { DatabasePath = databasePath },
            timeProvider);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            return ValueTask.CompletedTask;
        }
    }
}
