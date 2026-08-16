namespace Penghou.Cangjie.Sqlite;

/// <summary>Configures local SQLite context persistence.</summary>
public sealed class CangjieSqliteOptions
{
    /// <summary>Gets or sets the relative or absolute database file path.</summary>
    public required string DatabasePath { get; set; }

    /// <summary>Gets or sets whether write-ahead logging is enabled.</summary>
    public bool EnableWal { get; set; } = true;

    /// <summary>Gets or sets how long SQLite waits for a busy database.</summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
