using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static CrisisZonesApplication.Tests.TestData;

namespace CrisisZonesApplication.Tests;

/// <summary>In-process API tests against a small temporary event file.</summary>
public sealed class ApiTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"cz-test-{Guid.NewGuid():N}.geojson");

    public ApiTests()
    {
        File.WriteAllText(_file, Collection(
            Feature("POL1", "2024-09-02", "Warszawa"),
            Feature("POL2", "2024-10-02", "Warszawa", extra: new() { ["notes"] = "<script>alert(1)</script>" }),
            Feature("POL3", "2024-11-02", "Krakow", lat: 50.06, lng: 19.94, timestamp: Unix(2024, 12, 17))));
    }

    public void Dispose() => File.Delete(_file);

    private static HttpClient Client(string eventsFile) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:EventsFile"] = eventsFile,
                ["GoogleMaps:ApiKey"] = "",
            }));
        }).CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Event_list_is_compact_and_has_no_narrative_fields()
    {
        var r = await Client(_file).GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var list = await Json(r);
        Assert.Equal(3, list.GetArrayLength());
        var first = list[0];
        Assert.Equal("POL1", first.GetProperty("id").GetString());
        Assert.False(first.TryGetProperty("notes", out _));
        Assert.False(first.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Event_details_return_notes_as_plain_data()
    {
        var c = Client(_file);
        var e = await Json(await c.GetAsync("/api/events/pol2"));       // ids are case-insensitive
        Assert.Equal("<script>alert(1)</script>", e.GetProperty("notes").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/events/NOPE")).StatusCode);
    }

    [Fact]
    public async Task City_forecast_lookup_is_case_insensitive()
    {
        var c = Client(_file);
        var r = await c.GetAsync("/api/forecast/city/warszawa");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Json(r);
        Assert.Equal("2024-12", body.GetProperty("targetMonth").GetString());
        Assert.Equal(2.0 / 3, body.GetProperty("city").GetProperty("predicted").GetDouble(), 2);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/forecast/city/Atlantis")).StatusCode);
    }

    [Fact]
    public async Task Missing_data_gives_a_clear_503_not_a_crash()
    {
        var c = Client(Path.Combine(Path.GetTempPath(), "does-not-exist.geojson"));
        var meta = await Json(await c.GetAsync("/api/meta"));
        Assert.False(meta.GetProperty("available").GetBoolean());
        var r = await c.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Contains("fetch_acled.py", (await Json(r)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/api/forecast")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);         // the page itself still loads
    }

    [Fact]
    public async Task Only_successful_data_responses_are_cacheable()
    {
        var ok = await Client(_file).GetAsync("/api/events");
        Assert.Contains("max-age=300", ok.Headers.CacheControl!.ToString());
        var missing = Client(Path.Combine(Path.GetTempPath(), "does-not-exist.geojson"));
        foreach (var url in new[] { "/api/events", "/api/forecast" })
        {
            var r = await missing.GetAsync(url);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.False(r.Headers.CacheControl?.MaxAge > TimeSpan.Zero, $"{url} error response must not be cached");
        }
    }

    [Fact]
    public async Task Pages_send_security_headers_and_no_maps_key_when_unset()
    {
        var r = await Client(_file).GetAsync("/");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self' https://maps.googleapis.com", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("data-maps-key=\"\"", html);
        Assert.DoesNotContain("AIza", html);
    }

    [Fact]
    public async Task Unknown_pages_get_a_friendly_404_but_api_stays_json()
    {
        var c = Client(_file);
        var page = await c.GetAsync("/no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Contains("Page not found", await page.Content.ReadAsStringAsync());
        var api = await c.GetAsync("/api/events/NOPE");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Contains("Event not found", await api.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Raw_data_file_is_not_served_as_a_static_file()
    {
        var r = await Client(_file).GetAsync("/App_Data/conflict_data.geojson");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
