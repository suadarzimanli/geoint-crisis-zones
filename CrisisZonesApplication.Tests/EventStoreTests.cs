using CrisisZonesApplication.Services;
using static CrisisZonesApplication.Tests.TestData;

namespace CrisisZonesApplication.Tests;

public class EventStoreTests
{
    [Fact]
    public void Parses_acled_feature_with_coordinates_from_geometry()
    {
        var d = Parse(Collection(Feature("POL1", "2024-03-05", "Warszawa", lat: 52.2297, lng: 21.0122)));
        var e = Assert.Single(d.Events);
        Assert.Equal("POL1", e.Id);
        Assert.Equal(new DateOnly(2024, 3, 5), e.Date);
        Assert.Equal(52.2297, e.Latitude);
        Assert.Equal(21.0122, e.Longitude);
        Assert.Equal("Warszawa", e.Admin2);
        Assert.Equal(0, d.Skipped);
    }

    [Fact]
    public void Skips_bad_records_instead_of_failing()
    {
        var d = Parse(Collection(
            Feature("OK", "2024-01-10", "A"),
            Feature("", "2024-01-10", "A"),                                  // no id
            Feature("BADDATE", "not a date", "A"),                           // unreadable date
            Feature("NOWHERE", "2024-01-10", "A", lat: 0, lng: 0),           // null island
            Feature("OUT", "2024-01-10", "A", lat: 123, lng: 21),            // invalid latitude
            Feature("STRNUM", "2024-01-11", "A", extra: new() { ["fatalities"] = "2", ["geo_precision"] = "x" })));
        Assert.Equal(new[] { "OK", "STRNUM" }, d.Events.Select(e => e.Id));
        Assert.Equal(4, d.Skipped);
        Assert.Equal(2, d.Events[1].Fatalities);
        Assert.Equal(0, d.Events[1].GeoPrecision);
    }

    [Fact]
    public void Reads_epoch_millisecond_dates_from_gis_exports()
    {
        var ms = new DateTimeOffset(2023, 7, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var d = Parse(Collection(Feature("E", "x", "A", extra: new() { ["event_date"] = ms })));
        Assert.Equal(new DateOnly(2023, 7, 14), Assert.Single(d.Events).Date);
    }

    [Fact]
    public void Missing_county_falls_back_to_voivodeship()
    {
        var d = Parse(Collection(Feature("E", "2024-01-01", "", extra: new() { ["admin2"] = null })));
        Assert.Equal("Mazowieckie", Assert.Single(d.Events).Admin2);
    }

    [Fact]
    public void Extract_date_comes_from_newest_acled_timestamp()
    {
        var d = Parse(Collection(
            Feature("A", "2024-12-13", "X", timestamp: Unix(2024, 12, 17)),
            Feature("B", "2024-11-02", "X", timestamp: Unix(2024, 11, 20))));
        Assert.Equal(new DateOnly(2024, 12, 17), d.ExtractDate);
        Assert.Equal(new DateOnly(2024, 11, 1), d.LastCompleteMonth);   // December is still running
        Assert.Equal(new DateOnly(2024, 11, 2), d.FirstDate);
    }

    [Theory]
    [InlineData(2024, 12, 17, 2024, 11)]
    [InlineData(2024, 12, 31, 2024, 12)]
    [InlineData(2024, 2, 29, 2024, 2)]
    [InlineData(2025, 1, 1, 2024, 12)]
    public void Month_counts_as_complete_only_on_its_last_day(int y, int m, int d, int ey, int em) =>
        Assert.Equal(new DateOnly(ey, em, 1), EventStore.LastCompleteMonthOf(new DateOnly(y, m, d)));

    [Fact]
    public void Rejects_files_that_are_not_feature_collections()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{\"type\":\"Feature\"}"));
    }
}
