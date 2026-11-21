using System.Diagnostics;
using CrisisZonesApplication.Models;
using Microsoft.AspNetCore.Mvc;

namespace CrisisZonesApplication.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [Route("about")]
    public IActionResult About() => View();

    // Old link kept working
    [Route("Home/Privacy")]
    public IActionResult Privacy() => RedirectPermanent("/about#privacy");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [Route("status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code is >= 400 and < 600 ? code : 404;
        return View("Error", new ErrorViewModel { StatusCode = Response.StatusCode });
    }
}
