# Crisis Zones

A web application for exploring **ACLED protest and political violence events in Poland (2020–2024)**: from the national overview down to the record of a single event, with a transparent next-month forecast for every county. The repository also contains the forecast evaluation and the research paper.

> **Disclaimer.** Independent research and teaching project, not affiliated with ACLED. The app shows *reported* events; its forecasts are expectations from recent counts, not risk assessments.

## What it does

- **Explore.** All events on a map, grouped by proximity. Filter by event type, sub-type, voivodeship, county, period and the actors involved, or search for a place.
- **Every event at a place.** ACLED geocodes most events to a town or district centre: 95.5% of events share their coordinates with others, and one point in Warsaw holds 1,132. Selecting a place opens *all* its events, newest first, with ACLED's note, actors and source.
- **Forecast.** The expected number of events per county next month, computed as the average of the three latest complete months. Each value is shown with those three months, a 12-month sparkline and the events already recorded. The method's accuracy is listed next to naive and always-zero baselines.
- **Insights.** Charts of the monthly trend, event types, sub-types and the most active counties. They follow the filters.
- **Keyboard and no-map access.** "Read these events" opens the current selection (up to 2,000 events) one by one, newest first, without using the map.

Desktop and phone layouts: on phones the map stays on screen and the panel becomes a bottom sheet.

## Key results

These come from the paper (`paper/geoint-crisis-zones.pdf`). Next-month forecasts per county were tested on December 2023 to November 2024 (4,092 county-months). Each month is predicted only from earlier months, and every method sees the same history.

| Method | RMSE | MAE |
|---|---:|---:|
| Random Forest (lags + month) | 1.351 | 0.504 |
| **3-month moving average (shown in the app)** | **1.426** | **0.483** |
| Last month (naive) | 1.434 | 0.441 |
| Poisson GLM (lags) | 1.462 | 0.512 |
| Always zero | 1.852 | 0.419 |

- **No significant winner.** The RMSE difference between the moving average and the Random Forest is +0.07, and its 95% county-bootstrap interval (−0.08 to +0.23) includes zero. On a 6-month window the moving average beats the Random Forest.
- **Use RMSE, not MAE.** MAE rewards predicting no activity, because 85% of county-months have no events.
- **The evaluation design matters more than the method.** If months without events are dropped and the methods get different information, the same Random Forest looks 30% better than the moving average. When every method sees the same history, their errors on months with events are equal.

## Run it

Prerequisites:
- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer;
- a Google Maps JavaScript API key (optional, since Forecast and Insights work without it);
- ACLED data (see **Data**).

```bash
# 1. Event data (see Data below) -> CrisisZonesApplication/App_Data/conflict_data.geojson
python tools/fetch_acled.py --csv path/to/acled_export.csv

# 2. Google Maps key (git-ignored)
#    CrisisZonesApplication/appsettings.Development.local.json:
#    { "GoogleMaps": { "ApiKey": "YOUR_KEY" } }

# 3. Run
cd CrisisZonesApplication
dotnet run --urls http://localhost:5180
```

Open http://localhost:5180. Restrict the Maps key to your own site addresses in Google Cloud. The key needs the *Maps JavaScript API*, plus the *Geocoding API* for place search.

## Data

ACLED's terms do not allow raw event data to be redistributed, so this repository contains **no event records**. Create the app's local event file with your own ACLED account:

- **ACLED API** (if API access is enabled on your account): set `ACLED_EMAIL` and `ACLED_PASSWORD` in the environment, then run `python tools/fetch_acled.py --api`.
- **CSV export** from ACLED's [Data Export Tool](https://acleddata.com) (country Poland, 2020-01-01 to 2024-12-31): run `python tools/fetch_acled.py --csv export.csv`.

Credentials are read from the environment only and never written to disk. ACLED revises records over time, so a fresh download can differ slightly from the extract used in the paper. That extract reflects ACLED's records on 17 December 2024: 7,794 events, the last dated 13 December 2024.

The app treats a month as complete only if the extract was taken on or after its last day. A partly covered final month is shown as "so far" and never feeds the forecast.

**Attribution:** Armed Conflict Location & Event Data (ACLED); www.acleddata.com.

## Evaluation (reproducible without raw data)

`evaluation/data/city_month_counts.csv` holds the aggregated counts used in the paper: 341 counties × 59 complete months, with zero months included. They are derived counts that cannot be turned back into events.

```bash
cd evaluation
pip install -r requirements.txt
python run_evaluation.py          # -> results.md, results.json (about a minute)
python make_figures.py            # -> paper figures
# from your own extract instead:
python run_evaluation.py --from-geojson ../CrisisZonesApplication/App_Data/conflict_data.geojson
```

The backtest shown in the app is computed by the server from the same events and matches the script to three decimals.

## Tests

```bash
dotnet test CrisisZonesApplication.Tests
python -m unittest tools/test_fetch_acled.py
```

25 .NET tests cover:
- parsing and validation of event files (bad records are skipped and counted, not fatal);
- the complete-month rule, the moving average and the backtest;
- the API: compact list without narrative fields, case-insensitive lookups, a clear 503 when data are missing (never cached), security headers including the content security policy, no Maps key in the page when none is configured, raw data not served, and a friendly 404 page.

6 Python tests cover the download script: date formats (ISO and "13 December 2024"), invalid rows, period filtering and rejection of non-ACLED files.

## Project layout

```
CrisisZonesApplication/        ASP.NET Core 8 app (API + map UI)
  Services/EventStore.cs       loads and validates the local ACLED extract once
  Services/ForecastService.cs  monthly counts, moving-average forecast, backtest
  wwwroot/js/app.js            map, filters, event cards, forecast, charts
CrisisZonesApplication.Tests/  xUnit tests
evaluation/                    forecast evaluation + aggregated counts
tools/fetch_acled.py           builds the local event file from ACLED
paper/                         research paper (LaTeX + PDF)
```

## Privacy and security

- **Nothing about users is stored.** No accounts and no cookies. Google Maps and place search are provided by Google.
- **Safe display of data.** All text from the data is inserted as text, never as HTML.
- **Content security policy.** Scripts may only come from this site and Google Maps; no plugins, no framing.
- **Narrative fields on demand.** Notes, actors and sources are only sent for the event being viewed.
- **No secrets in the repository.** The Maps key lives in a git-ignored local file.

## Licence

Code: MIT. ACLED data remain subject to [ACLED's terms of use](https://acleddata.com/eula).
