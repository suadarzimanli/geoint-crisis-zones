using CrisisZonesApplication.Services;
using static CrisisZonesApplication.Tests.TestData;

namespace CrisisZonesApplication.Tests;

public class ForecastTests
{
    [Theory]
    [InlineData(new[] { 19, 17, 14 }, 50.0 / 3)]
    [InlineData(new[] { 5, 0, 0, 3 }, 1.0)]   // only the last three months count
    [InlineData(new[] { 4 }, 4.0)]            // shorter history: mean of what exists
    [InlineData(new int[0], 0.0)]
    public void Moving_average_uses_the_last_three_months(int[] series, double expected) =>
        Assert.Equal(expected, ForecastService.MovingAverage(series), 9);

    [Fact]
    public void Monthly_counts_include_months_without_events()
    {
        var d = Parse(Collection(
            Feature("1", "2024-01-03", "A"), Feature("2", "2024-01-20", "A"), Feature("3", "2024-03-09", "A")));
        var months = ForecastService.MonthRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1));
        Assert.Equal(new[] { 2, 0, 1 }, ForecastService.MonthlyCounts(d.Events, months)["Mazowieckie|A"]);
    }

    [Fact]
    public void Forecast_ignores_the_incomplete_last_month_but_reports_it()
    {
        // Sep–Nov complete; data extracted on 17 Dec with two December events
        var d = Parse(Collection(
            Feature("s1", "2024-09-02", "A"), Feature("s2", "2024-09-20", "A"), Feature("s3", "2024-09-21", "A"),
            Feature("o1", "2024-10-05", "A"),
            Feature("n1", "2024-11-05", "A"), Feature("n2", "2024-11-06", "A"),
            Feature("d1", "2024-12-01", "A"), Feature("d2", "2024-12-13", "A", timestamp: Unix(2024, 12, 17)),
            Feature("b1", "2024-10-10", "B", sub: "Mob violence", type: "Riots")));
        var r = ForecastService.Compute(d);
        Assert.Equal("2024-12", r.TargetMonth);
        Assert.Equal(new[] { "2024-09", "2024-10", "2024-11" }, r.BasisMonths);
        Assert.Equal("2024-12-13", r.PartialUntil);

        var a = r.Cities.Single(c => c.City == "A");
        Assert.Equal(2.0, a.Predicted);                    // (3 + 1 + 2) / 3, December not used
        Assert.Equal(new[] { 3, 1, 2 }, a.Basis);
        Assert.Equal(2, a.SoFar);
        Assert.Equal(8, a.Total);
        Assert.Equal(r.Cities[0], a);                       // sorted by forecast

        var b = r.Cities.Single(c => c.City == "B");
        Assert.Equal(0.33, b.Predicted);
        Assert.Equal(0.33, b.PredictedSubTypes["Mob violence"]);
        Assert.Equal(0.0, b.ProtestShare);
    }

    [Fact]
    public void Counties_with_the_same_name_in_different_voivodeships_stay_separate()
    {
        var lubelskie = new Dictionary<string, object?> { ["admin1"] = "Lubelskie" };
        var lodzkie = new Dictionary<string, object?> { ["admin1"] = "Lodzkie" };
        var d = Parse(Collection(
            Feature("1", "2024-09-02", "Tomaszowski", extra: lubelskie), Feature("2", "2024-10-02", "Tomaszowski", extra: lubelskie),
            Feature("3", "2024-11-02", "Tomaszowski", extra: lubelskie),
            Feature("4", "2024-11-03", "Tomaszowski", extra: lodzkie, timestamp: Unix(2024, 12, 17))));
        var r = ForecastService.Compute(d);
        Assert.Equal(2, r.Cities.Count(c => c.City == "Tomaszowski"));
        var lub = r.Cities.Single(c => c.Region == "Lubelskie");
        var lod = r.Cities.Single(c => c.Region == "Lodzkie");
        Assert.Equal(new[] { 1, 1, 1 }, lub.Basis);
        Assert.Equal(new[] { 0, 0, 1 }, lod.Basis);
        Assert.Equal("Lodzkie|Tomaszowski", lod.Key);
    }

    [Fact]
    public void A_later_download_does_not_add_empty_months_after_the_data()
    {
        // 2020–2024 events downloaded in 2026: ACLED timestamps are from 2026
        var d = Parse(Collection(
            Feature("a", "2024-10-10", "A"), Feature("b", "2024-11-10", "A"),
            Feature("c", "2024-12-20", "A", timestamp: Unix(2026, 3, 5))));
        Assert.Equal(new DateOnly(2024, 12, 1), d.LastCompleteMonth);
        var r = ForecastService.Compute(d);
        Assert.Equal("2025-01", r.TargetMonth);
        Assert.Equal(new[] { 1, 1, 1 }, r.Cities.Single().Basis);
        Assert.Null(r.PartialUntil);
    }

    [Fact]
    public void Backtest_predicts_each_month_from_earlier_months_only()
    {
        // one county, months: 3 3 3 6 0 ; test the last 2 months
        var s = new[] { new[] { 3, 3, 3, 6, 0 } };
        var scores = ForecastService.Backtest(s, testMonths: 2);
        var ma = scores[0];      // predictions: 3 (for 6), 4 (for 0) -> errors 3, 4
        Assert.Equal(3.5, ma.Mae, 9);
        Assert.Equal(Math.Sqrt((9 + 16) / 2.0), ma.Rmse, 9);
        var naive = scores[1];   // predictions: 3, 6 -> errors 3, 6
        Assert.Equal(4.5, naive.Mae, 9);
        var zero = scores[2];    // errors 6, 0
        Assert.Equal(3.0, zero.Mae, 9);
    }
}
