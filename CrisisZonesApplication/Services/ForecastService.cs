using CrisisZonesApplication.Models;

namespace CrisisZonesApplication.Services;

public sealed record CityForecast(
    string City,
    string Region,
    double Predicted,
    IReadOnlyList<int> Basis,                       // the three months the forecast averages
    IReadOnlyList<int> Last12,                      // last 12 complete months, oldest first
    int Total,
    double ProtestShare,
    IReadOnlyDictionary<string, double> PredictedSubTypes,
    int SoFar);                                     // events already recorded in the target month

public sealed record BacktestScore(string Method, double Mae, double Rmse);

public sealed record ForecastResult(
    string TargetMonth,                             // yyyy-MM
    IReadOnlyList<string> BasisMonths,
    string LastCompleteMonth,
    string? PartialUntil,                           // last event date inside the target month, if any
    int Window,
    int BacktestMonths,
    IReadOnlyList<BacktestScore> Backtest,
    IReadOnlyList<CityForecast> Cities);

/// <summary>
/// Per-city monthly event counts and the k-month moving-average forecast shown in the app.
/// Months are calendar months; months without events count as zero. Only complete months
/// (up to <see cref="EventData.LastCompleteMonth"/>) feed the forecast and the backtest.
/// </summary>
public sealed class ForecastService
{
    public const int Window = 3;
    public const int BacktestMonths = 12;

    private readonly EventStore _store;
    private readonly Lazy<ForecastResult?> _result;

    public ForecastService(EventStore store)
    {
        _store = store;
        _result = new Lazy<ForecastResult?>(() => store.IsAvailable ? Compute(store.Data) : null);
    }

    public ForecastResult? Get() => _result.Value;

    /// <summary>Mean of the last <paramref name="window"/> values (fewer if the series is shorter; 0 if empty).</summary>
    public static double MovingAverage(IReadOnlyList<int> series, int window = Window)
    {
        if (series.Count == 0) return 0;
        int n = Math.Min(window, series.Count);
        double sum = 0;
        for (int i = series.Count - n; i < series.Count; i++) sum += series[i];
        return sum / n;
    }

    /// <summary>Months from <paramref name="first"/> to <paramref name="last"/> inclusive (first days).</summary>
    public static List<DateOnly> MonthRange(DateOnly first, DateOnly last)
    {
        var list = new List<DateOnly>();
        for (var m = new DateOnly(first.Year, first.Month, 1); m <= last; m = m.AddMonths(1)) list.Add(m);
        return list;
    }

    /// <summary>City → monthly counts aligned to <paramref name="months"/> (zeros included).</summary>
    public static Dictionary<string, int[]> MonthlyCounts(IEnumerable<ConflictEvent> events, IReadOnlyList<DateOnly> months)
    {
        var index = months.Select((m, i) => (m, i)).ToDictionary(t => t.m, t => t.i);
        var counts = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!counts.TryGetValue(e.Admin2, out var row)) counts[e.Admin2] = row = new int[months.Count];
            if (index.TryGetValue(new DateOnly(e.Date.Year, e.Date.Month, 1), out var i)) row[i]++;
        }
        return counts;
    }

    /// <summary>
    /// One-step-ahead backtest over the last <paramref name="testMonths"/> months of every city:
    /// each month is predicted from the observed months before it.
    /// </summary>
    public static IReadOnlyList<BacktestScore> Backtest(IReadOnlyCollection<int[]> series, int testMonths, int window = Window)
    {
        double maeMa = 0, mseMa = 0, maeNaive = 0, mseNaive = 0, maeZero = 0, mseZero = 0;
        int n = 0;
        foreach (var s in series)
        {
            for (int t = Math.Max(window, s.Length - testMonths); t < s.Length; t++)
            {
                double ma = 0;
                for (int k = 1; k <= window; k++) ma += s[t - k];
                ma /= window;
                double y = s[t], naive = s[t - 1];
                maeMa += Math.Abs(y - ma); mseMa += (y - ma) * (y - ma);
                maeNaive += Math.Abs(y - naive); mseNaive += (y - naive) * (y - naive);
                maeZero += y; mseZero += y * y;
                n++;
            }
        }
        if (n == 0) return Array.Empty<BacktestScore>();
        return new[]
        {
            new BacktestScore($"{window}-month moving average", maeMa / n, Math.Sqrt(mseMa / n)),
            new BacktestScore("Last month (naive)", maeNaive / n, Math.Sqrt(mseNaive / n)),
            new BacktestScore("Always zero", maeZero / n, Math.Sqrt(mseZero / n)),
        };
    }

    public static ForecastResult Compute(EventData data)
    {
        var months = MonthRange(data.FirstDate, data.LastCompleteMonth);
        var complete = data.Events.Where(e => e.Date < data.LastCompleteMonth.AddMonths(1)).ToList();
        var counts = MonthlyCounts(complete, months);
        var target = data.LastCompleteMonth.AddMonths(1);
        var basisStart = months.Count >= Window ? months.Count - Window : 0;
        var basisMonths = months.Skip(basisStart).ToList();

        var byCity = data.Events.GroupBy(e => e.Admin2, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var cities = new List<CityForecast>();
        foreach (var (city, all) in byCity)
        {
            var series = counts.TryGetValue(city, out var s) ? s : new int[months.Count];
            var region = all.GroupBy(e => e.Admin1).OrderByDescending(g => g.Count()).First().Key;
            var basisFrom = basisMonths.Count > 0 ? basisMonths[0] : target;
            var subTypes = all.Where(e => e.Date >= basisFrom && e.Date < target)
                              .GroupBy(e => e.SubEventType)
                              .ToDictionary(g => g.Key, g => Math.Round(g.Count() / (double)Math.Max(1, basisMonths.Count), 2));
            cities.Add(new CityForecast(
                city, region,
                Math.Round(MovingAverage(series), 2),
                series.Skip(basisStart).ToArray(),
                series.Skip(Math.Max(0, series.Length - 12)).ToArray(),
                all.Count,
                Math.Round(100.0 * all.Count(e => e.EventType == "Protests") / all.Count, 1),
                subTypes.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                all.Count(e => e.Date >= target && e.Date < target.AddMonths(1))));
        }

        var inTarget = data.Events.Where(e => e.Date >= target && e.Date < target.AddMonths(1)).ToList();
        return new ForecastResult(
            target.ToString("yyyy-MM"),
            basisMonths.Select(m => m.ToString("yyyy-MM")).ToList(),
            data.LastCompleteMonth.ToString("yyyy-MM"),
            inTarget.Count > 0 ? inTarget.Max(e => e.Date).ToString("yyyy-MM-dd") : null,
            Window,
            BacktestMonths,
            Backtest(counts.Values, BacktestMonths),
            cities.OrderByDescending(c => c.Predicted).ThenByDescending(c => c.Total).ThenBy(c => c.City, StringComparer.Ordinal).ToList());
    }
}
