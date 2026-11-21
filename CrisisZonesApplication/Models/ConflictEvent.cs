namespace CrisisZonesApplication.Models;

/// <summary>One ACLED event as loaded from the local GeoJSON extract.</summary>
public sealed record ConflictEvent
{
    public required string Id { get; init; }            // event_id_cnty
    public required DateOnly Date { get; init; }        // event_date
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string EventType { get; init; } = "Unknown";
    public string SubEventType { get; init; } = "Unknown";
    public string DisorderType { get; init; } = "";
    public string Interaction { get; init; } = "";
    public string Admin1 { get; init; } = "";           // voivodeship
    public string Admin2 { get; init; } = "Unknown";    // county / city (powiat)
    public string Admin3 { get; init; } = "";
    public string Location { get; init; } = "";
    public string Actor1 { get; init; } = "";
    public string AssocActor1 { get; init; } = "";
    public string Actor2 { get; init; } = "";
    public string AssocActor2 { get; init; } = "";
    public string CivilianTargeting { get; init; } = "";
    public string Source { get; init; } = "";
    public string SourceScale { get; init; } = "";
    public string Notes { get; init; } = "";
    public int Fatalities { get; init; }
    public int GeoPrecision { get; init; }
    public int TimePrecision { get; init; }
    /// <summary>Unix seconds of ACLED's last update of the record (0 if absent).</summary>
    public long Timestamp { get; init; }
}
