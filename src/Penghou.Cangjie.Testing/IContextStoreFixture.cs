namespace Penghou.Cangjie.Testing;

/// <summary>Provides stores sharing one durable backing store for conformance checks.</summary>
public interface IContextStoreFixture : IAsyncDisposable
{
    /// <summary>Gets the primary store used to create conformance data.</summary>
    IContextStore Store { get; }

    /// <summary>
    /// Creates a fresh store instance over the same durable data, simulating a
    /// new process or consumer.
    /// </summary>
    IContextStore CreatePeerStore();
}
