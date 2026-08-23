namespace Penghou.Cangjie.Tests;

public sealed class ContextModelTests
{
    [Fact]
    public void QueryDefaults_AreSafeAndDeterministic()
    {
        var query = new ContextQuery();

        Assert.Equal(10, query.Limit);
        Assert.Equal(ContextSearchMode.AllTerms, query.SearchMode);
        Assert.False(query.IncludeExpired);
        Assert.Null(query.Scopes);
    }

    [Fact]
    public void RelationKinds_AreStableTextIdentifiers()
    {
        Assert.Equal("derived-from", ContextRelationKinds.DerivedFrom);
        Assert.Equal("supersedes", ContextRelationKinds.Supersedes);
    }

    [Fact]
    public void SearchStrategies_AreStableTextIdentifiers()
    {
        Assert.Equal("exact", ContextSearchStrategies.Exact);
        Assert.Equal("lexical", ContextSearchStrategies.Lexical);
        Assert.Equal("Penghou.Cangjie", CangjieDiagnostics.ActivitySourceName);
    }

    [Fact]
    public void RelationQueryDefaults_AreBoundedAndDirectional()
    {
        var query = new ContextRelationQuery();

        Assert.Equal(ContextRelationDirection.Outgoing, query.Direction);
        Assert.Equal(100, query.Limit);
        Assert.Null(query.Kinds);
    }

    [Fact]
    public void ContextKinds_AreStableButConsumerExtensible()
    {
        Assert.Equal("evidence", ContextKinds.Evidence);

        var item = new ContextItem
        {
            Scope = "test",
            Kind = "solo:constraint",
            Content = "Keep the boundary provider-neutral.",
            Provenance = new ContextProvenance { Producer = "tests" }
        };

        Assert.Equal("solo:constraint", item.Kind);
    }

    [Fact]
    public void CoreAssembly_HasNoProviderOrWorkflowDependency()
    {
        var references = typeof(ContextItem).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name is not null &&
            (name.Contains("Baize", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Zhinu", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)));
    }
}
