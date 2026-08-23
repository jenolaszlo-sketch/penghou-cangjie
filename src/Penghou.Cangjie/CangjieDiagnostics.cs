namespace Penghou.Cangjie;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>Exposes privacy-safe diagnostic activities and metrics emitted by Cangjie.</summary>
public static class CangjieDiagnostics
{
    /// <summary>Identifies the Cangjie diagnostic activity source.</summary>
    public const string ActivitySourceName = "Penghou.Cangjie";

    /// <summary>Identifies the Cangjie metric meter.</summary>
    public const string MeterName = "Penghou.Cangjie";

    /// <summary>Gets the activity source used for store diagnostics.</summary>
    public static ActivitySource ActivitySource { get; } =
        new(ActivitySourceName);

    /// <summary>Gets the meter used for store metrics.</summary>
    public static Meter Meter { get; } = new(MeterName);

    /// <summary>Counts context items durably stored (one per committed revision).</summary>
    public static Counter<long> ItemsStored { get; } =
        Meter.CreateCounter<long>("cangjie.items.stored");

    /// <summary>Counts physically deleted expired standalone items.</summary>
    public static Counter<long> ExpiredDeleted { get; } =
        Meter.CreateCounter<long>("cangjie.expired.deleted");

    /// <summary>Measures lexical/indexed search duration in seconds.</summary>
    public static Histogram<double> SearchDuration { get; } =
        Meter.CreateHistogram<double>("cangjie.search.duration", "s");
}
