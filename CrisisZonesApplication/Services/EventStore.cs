using System.Globalization;
using System.Text.Json;
using CrisisZonesApplication.Models;

namespace CrisisZonesApplication.Services;

/// <summary>Result of reading an ACLED GeoJSON extract.</summary>
public sealed class EventData
{
    public required IReadOnlyList<ConflictEvent> Events { get; init; }
    public int Skipped { get; init; }
    public DateOnly FirstDate { get; init; }
    public DateOnly LastDate { get; init; }
    /// <summary>Date of the extract: the newest ACLED update timestamp, or the last event date if absent.</summary>
    public DateOnly ExtractDate { get; init; }
    /// <summary>First day of the last calendar month fully covered by the extract.</summary>
    public DateOnly LastCompleteMonth { get; init; }

    public static readonly EventData Empty = new() { Events = Array.Empty<ConflictEvent>() };
}

/// <summary>
/// Loads the local event extract once and keeps it in memory. The file is not part of the
/// repository (ACLED terms do not allow redistributing raw records); see tools/fetch_acled.py.
/// </summary>
public sealed class EventStore
{
    private readonly Lazy<(EventData Data, string? Error)> _load;
    private Dictionary<string, ConflictEvent>? _byId;

    public EventStore(IConfiguration config, IWebHostEnvironment env, ILogger<EventStore> log)
    {
        var configured = config["Data:EventsFile"] ?? "App_Data/conflict_data.geojson";
        Path = System.IO.Path.IsPathRooted(configured) ? configured : System.IO.Path.Combine(env.ContentRootPath, configured);
        _load = new Lazy<(EventData, string?)>(() =>
        {
            if (!File.Exists(Path))
            {
                log.LogWarning("Event data not found at {Path}. See README, section Data.", Path);
                return (EventData.Empty, "Event data not found. Download it with tools/fetch_acled.py (see README, section Data).");
            }
            try
            {
                using var s = File.OpenRead(Path);
                var data = Parse(s);
                log.LogInformation("Loaded {Count} events ({Skipped} skipped) from {Path}", data.Events.Count, data.Skipped, Path);
                return (data, data.Events.Count == 0 ? "The event file contains no usable events." : null);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                log.LogError(ex, "Could not read event data from {Path}", Path);
                return (EventData.Empty, "The event file could not be read. Re-create it with tools/fetch_acled.py.");
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal EventStore(EventData data)
    {
        Path = "(memory)";
        _load = new Lazy<(EventData, string?)>(() => (data, data.Events.Count == 0 ? "No events." : null));
    }

    public string Path { get; }
    public EventData Data => _load.Value.Data;
    public string? Error => _load.Value.Error;
    public bool IsAvailable => Error is null;

    public ConflictEvent? Find(string id)
    {
        _byId ??= Data.Events.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _byId.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>
    /// Parses a GeoJSON FeatureCollection of ACLED events. Features with a missing id,
    /// an unreadable date or coordinates outside the valid range are skipped, not fatal.
    /// </summary>
    public static EventData Parse(Stream geojson)
    {
        using var doc = JsonDocument.Parse(geojson);
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("Not a GeoJSON FeatureCollection.");

        var events = new List<ConflictEvent>(features.GetArrayLength());
        int skipped = 0;
        long maxTimestamp = 0;
        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) { skipped++; continue; }
            var id = Str(p, "event_id_cnty");
            var date = ReadDate(p);
            var (lat, lng) = ReadPosition(f, p);
            if (string.IsNullOrWhiteSpace(id) || date is null || lat is null || lng is null) { skipped++; continue; }

            var ts = Long(p, "timestamp");
            maxTimestamp = Math.Max(maxTimestamp, ts);
            events.Add(new ConflictEvent
            {
                Id = id.Trim(),
                Date = date.Value,
                Latitude = lat.Value,
                Longitude = lng.Value,
                EventType = Or(Str(p, "event_type"), "Unknown"),
                SubEventType = Or(Str(p, "sub_event_type"), "Unknown"),
                DisorderType = Str(p, "disorder_type"),
                Interaction = Str(p, "interaction"),
                Admin1 = Str(p, "admin1"),
                Admin2 = Or(Str(p, "admin2"), Or(Str(p, "admin1"), "Unknown")),
                Admin3 = Str(p, "admin3"),
                Location = Str(p, "location"),
                Actor1 = Str(p, "actor1"),
                AssocActor1 = Str(p, "assoc_actor_1"),
                Actor2 = Str(p, "actor2"),
                AssocActor2 = Str(p, "assoc_actor_2"),
                CivilianTargeting = Str(p, "civilian_targeting"),
                Source = Str(p, "source"),
                SourceScale = Str(p, "source_scale"),
                Notes = Str(p, "notes"),
                Fatalities = Math.Max(0, (int)Long(p, "fatalities")),
                GeoPrecision = (int)Long(p, "geo_precision"),
                TimePrecision = (int)Long(p, "time_precision"),
                Timestamp = ts,
            });
        }
        events.Sort((a, b) => a.Date.CompareTo(b.Date));
        if (events.Count == 0) return new EventData { Events = events, Skipped = skipped };

        var last = events[^1].Date;
        var extract = maxTimestamp > 0 ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(maxTimestamp).UtcDateTime) : last;
        if (extract < last) extract = last;
        return new EventData
        {
            Events = events,
            Skipped = skipped,
            FirstDate = events[0].Date,
            LastDate = last,
            ExtractDate = extract,
            LastCompleteMonth = LastCompleteMonthOf(extract),
        };
    }

    /// <summary>The month containing <paramref name="extract"/> is complete only if the extract is from its last day.</summary>
    public static DateOnly LastCompleteMonthOf(DateOnly extract)
    {
        var month = new DateOnly(extract.Year, extract.Month, 1);
        return extract.Day == DateTime.DaysInMonth(extract.Year, extract.Month) ? month : month.AddMonths(-1);
    }

    private static string Or(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s;

    private static string Str(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Trim() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        } : "";

    private static long Long(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (long)d;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
        return 0;
    }

    private static DateOnly? ReadDate(JsonElement p)
    {
        if (!p.TryGetProperty("event_date", out var v)) return null;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString() ?? "";
            if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : null;
        }
        // ArcGIS exports dates as epoch milliseconds
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms) && ms > 0)
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);
        return null;
    }

    private static (double?, double?) ReadPosition(JsonElement feature, JsonElement p)
    {
        double? lat = null, lng = null;
        if (feature.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object &&
            g.TryGetProperty("coordinates", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 2 &&
            c[0].TryGetDouble(out var x) && c[1].TryGetDouble(out var y))
        { lng = x; lat = y; }
        else if (double.TryParse(Str(p, "latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
                 double.TryParse(Str(p, "longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
        { lat = la; lng = lo; }
        if (lat is null || lng is null || double.IsNaN(lat.Value) || double.IsNaN(lng.Value) ||
            lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0)) return (null, null);
        return (Math.Round(lat.Value, 5), Math.Round(lng.Value, 5));
    }
}
