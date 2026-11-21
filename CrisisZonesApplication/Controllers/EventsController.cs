using CrisisZonesApplication.Services;
using Microsoft.AspNetCore.Mvc;

namespace CrisisZonesApplication.Controllers;

[ApiController]
[Route("api")]
public sealed class EventsController : ControllerBase
{
    private readonly EventStore _store;
    public EventsController(EventStore store) => _store = store;

    /// <summary>Dataset status: coverage, extract date and counts.</summary>
    [HttpGet("meta")]
    public IActionResult Meta()
    {
        var d = _store.Data;
        return Ok(new
        {
            available = _store.IsAvailable,
            error = _store.Error,
            count = d.Events.Count,
            skipped = d.Skipped,
            firstDate = d.Events.Count > 0 ? d.FirstDate.ToString("yyyy-MM-dd") : null,
            lastDate = d.Events.Count > 0 ? d.LastDate.ToString("yyyy-MM-dd") : null,
            extractDate = d.Events.Count > 0 ? d.ExtractDate.ToString("yyyy-MM-dd") : null,
            lastCompleteMonth = d.Events.Count > 0 ? d.LastCompleteMonth.ToString("yyyy-MM") : null,
            source = "ACLED (Armed Conflict Location & Event Data), www.acleddata.com",
        });
    }

    /// <summary>
    /// All events in a compact form for the map, filters and charts. Narrative fields
    /// (notes, actors, sources) are only returned per event by <see cref="Event"/>.
    /// </summary>
    [HttpGet("events")]
    public IActionResult Events()
    {
        if (!_store.IsAvailable) return Unavailable();
        CacheFor5Minutes();   // successful responses only; an error must not be cached
        return Ok(_store.Data.Events.Select(e => new
        {
            id = e.Id,
            lat = e.Latitude,
            lng = e.Longitude,
            date = e.Date.ToString("yyyy-MM-dd"),
            type = e.EventType,
            sub = e.SubEventType,
            region = e.Admin1,
            city = e.Admin2,
            interaction = e.Interaction,
            fatalities = e.Fatalities,
        }));
    }

    /// <summary>Full record of one event (shown when a marker is selected).</summary>
    [HttpGet("events/{id}")]
    public IActionResult Event(string id)
    {
        if (!_store.IsAvailable) return Unavailable();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 40) return NotFound();
        var e = _store.Find(id);
        if (e is null) return NotFound(new { error = "Event not found." });
        return Ok(new
        {
            id = e.Id,
            date = e.Date.ToString("yyyy-MM-dd"),
            lat = e.Latitude,
            lng = e.Longitude,
            disorderType = e.DisorderType,
            type = e.EventType,
            sub = e.SubEventType,
            interaction = e.Interaction,
            actor1 = e.Actor1,
            assocActor1 = e.AssocActor1,
            actor2 = e.Actor2,
            assocActor2 = e.AssocActor2,
            civilianTargeting = e.CivilianTargeting,
            region = e.Admin1,
            city = e.Admin2,
            admin3 = e.Admin3,
            location = e.Location,
            geoPrecision = e.GeoPrecision,
            timePrecision = e.TimePrecision,
            fatalities = e.Fatalities,
            source = e.Source,
            sourceScale = e.SourceScale,
            notes = e.Notes,
        });
    }

    private void CacheFor5Minutes() => Response.Headers.CacheControl = "public,max-age=300";

    private ObjectResult Unavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = _store.Error });
}
