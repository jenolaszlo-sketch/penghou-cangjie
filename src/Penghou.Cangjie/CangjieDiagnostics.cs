namespace Penghou.Cangjie;

/// <summary>Contains stable names for privacy-safe Cangjie diagnostics.</summary>
public static class CangjieDiagnostics
{
    /// <summary>Identifies the Cangjie diagnostic activity source.</summary>
    public const string ActivitySourceName = "Penghou.Cangjie";

    /// <summary>Identifies the Cangjie metric meter.</summary>
    public const string MeterName = "Penghou.Cangjie";

    /// <summary>Identifies the committed-item counter.</summary>
    public const string ItemsStoredInstrumentName = "cangjie.items.stored";

    /// <summary>Identifies the expired-item deletion counter.</summary>
    public const string ExpiredDeletedInstrumentName = "cangjie.expired.deleted";

    /// <summary>Identifies the search-duration histogram.</summary>
    public const string SearchDurationInstrumentName = "cangjie.search.duration";
}
