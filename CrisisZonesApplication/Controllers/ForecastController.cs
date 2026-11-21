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
    [ResponseCache(Duration = 300)]
    public IActionResult Get()
    {
        var result = _forecasts.Get();
        return result is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = _store.Error })
            : Ok(result);
    }

    /// <summary>Forecast for one city (case-insensitive name).</summary>
    [HttpGet("city/{name}")]
    public IActionResult City(string name)
    {
        var result = _forecasts.Get();
        if (result is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = _store.Error });
        var city = result.Cities.FirstOrDefault(c => string.Equals(c.City, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        return city is null ? NotFound(new { error = $"No forecast for '{name}'." }) : Ok(new { result.TargetMonth, result.BasisMonths, city });
    }
}
