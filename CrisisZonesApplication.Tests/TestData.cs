using System.Text;
using System.Text.Json;
using CrisisZonesApplication.Services;

namespace CrisisZonesApplication.Tests;

internal static class TestData
{
    /// <summary>Builds one GeoJSON feature with ACLED-style properties; <paramref name="extra"/> overrides fields.</summary>
    public static object Feature(string id, string date, string city, double lat = 52.23, double lng = 21.01,
        string type = "Protests", string sub = "Peaceful protest", long timestamp = 0, Dictionary<string, object?>? extra = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["event_id_cnty"] = id,
            ["event_date"] = date,
            ["event_type"] = type,
            ["sub_event_type"] = sub,
            ["interaction"] = "Protesters only",
            ["admin1"] = "Mazowieckie",
            ["admin2"] = city,
            ["location"] = city,
            ["notes"] = $"Note for {id}",
            ["source"] = "Example",
            ["fatalities"] = 0,
            ["timestamp"] = timestamp,
        };
        if (extra is not null) foreach (var (k, v) in extra) props[k] = v;
        return new { type = "Feature", geometry = new { type = "Point", coordinates = new[] { lng, lat } }, properties = props };
    }

    public static string Collection(params object[] features) =>
        JsonSerializer.Serialize(new { type = "FeatureCollection", features });

    public static EventData Parse(string json) => EventStore.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    public static long Unix(int y, int m, int d) => new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
}
