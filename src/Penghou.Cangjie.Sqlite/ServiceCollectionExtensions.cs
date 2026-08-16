using Microsoft.Extensions.DependencyInjection;

namespace Penghou.Cangjie.Sqlite;

/// <summary>Provides optional dependency-injection registration.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers one thread-safe SQLite context store.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">Configures the database path and SQLite behavior.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddCangjieSqlite(
        this IServiceCollection services,
        Action<CangjieSqliteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CangjieSqliteOptions { DatabasePath = string.Empty };
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        services.AddSingleton(options);
        services.AddSingleton<SqliteContextStore>();
        services.AddSingleton<IContextStore>(provider =>
            provider.GetRequiredService<SqliteContextStore>());
        return services;
    }
}
