using CrisisZonesApplication.Services;
using Microsoft.AspNetCore.Mvc;

namespace CrisisZonesApplication.Controllers;

[ApiController]
[Route("api/forecast")]
public sealed class ForecastController : ControllerBase
{
    private readonly ForecastService _forecasts;
    private readonly EventStore _store;

    public ForecastController(ForecastService forecasts, EventStore store)
    {
        _forecasts = forecasts;
        _store = store;
    }

    /// <summary>Next-month forecast for every city, highest first, with the backtest of the method.</summary>
    [HttpGet]
    public IActionResult Get()
    {
        var result = _forecasts.Get();
        if (result is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = _store.Error });
        Response.Headers.CacheControl = "public,max-age=300";   // successful responses only
        return Ok(result);
    }

    /// <summary>
    /// Forecast for one county (case-insensitive name). County names repeat across voivodeships;
    /// pass <c>?region=</c> to choose one, otherwise an ambiguous name answers 409 with the options.
    /// </summary>
    [HttpGet("city/{name}")]
    public IActionResult City(string name, [FromQuery] string? region = null)
    {
        var result = _forecasts.Get();
        if (result is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = _store.Error });
        var matches = result.Cities
            .Where(c => string.Equals(c.City, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrWhiteSpace(region) || string.Equals(c.Region, region.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            0 => NotFound(new { error = $"No forecast for '{name}'." }),
            1 => Ok(new { result.TargetMonth, result.BasisMonths, city = matches[0] }),
            _ => Conflict(new { error = $"'{name}' exists in several voivodeships; add ?region=.", regions = matches.Select(c => c.Region) }),
        };
    }
}
