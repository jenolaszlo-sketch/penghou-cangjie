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
    }

    [Fact]
    public void RelationKinds_AreStableTextIdentifiers()
    {
        Assert.Equal("derived-from", ContextRelationKinds.DerivedFrom);
        Assert.Equal("supersedes", ContextRelationKinds.Supersedes);
    }
}
