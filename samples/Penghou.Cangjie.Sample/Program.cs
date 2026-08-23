using Penghou.Cangjie;
using Penghou.Cangjie.Sqlite;

var store = new SqliteContextStore(new CangjieSqliteOptions
{
    DatabasePath = "cangjie.db"
});

var evidence = await store.StoreAsync(new ContextItem
{
    Scope = "repo:penghou-baize",
    Kind = ContextKinds.Evidence,
    Content = "The Gateway project references Yarp.ReverseProxy.",
    Provenance = new ContextProvenance
    {
        Producer = "sample:research",
        Source = new ContextSource
        {
            Uri = "repo://src/Gateway/Gateway.csproj"
        }
    },
    Tags = ["architecture", "gateway"]
});

var knowledge = await store.StoreAsync(new ContextItem
{
    Scope = "repo:penghou-baize",
    Key = "knowledge:reverse-proxy",
    Kind = ContextKinds.Knowledge,
    Content = "The application uses YARP as its reverse proxy.",
    Provenance = new ContextProvenance { Producer = "sample:analysis" }
});

await store.AddRelationAsync(new ContextRelation
{
    FromId = knowledge.Id,
    ToId = evidence.Id,
    Kind = ContextRelationKinds.DerivedFrom
});

var results = await store.SearchAsync(new ContextQuery
{
    Scope = "repo:penghou-baize",
    Text = "reverse proxy YARP"
});

foreach (var result in results)
{
    Console.WriteLine($"{result.Rank}: {result.Item.Content}");
    Console.WriteLine(
        $"Source: {result.Item.Provenance.Source?.Uri ?? "derived context"}");
}
