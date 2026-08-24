using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Penghou.Cangjie.Sqlite;

internal static class CangjieSqliteDiagnostics
{
    private static readonly Meter Meter = new(CangjieDiagnostics.MeterName);

    public static ActivitySource ActivitySource { get; } =
        new(CangjieDiagnostics.ActivitySourceName);

    public static Counter<long> ItemsStored { get; } =
        Meter.CreateCounter<long>(CangjieDiagnostics.ItemsStoredInstrumentName);

    public static Counter<long> ExpiredDeleted { get; } =
        Meter.CreateCounter<long>(CangjieDiagnostics.ExpiredDeletedInstrumentName);

    public static Histogram<double> SearchDuration { get; } =
        Meter.CreateHistogram<double>(
            CangjieDiagnostics.SearchDurationInstrumentName,
            "s");
}
