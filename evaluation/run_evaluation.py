#!/usr/bin/env python3
"""
Forecast evaluation for Crisis Zones: monthly ACLED event counts per Polish county (admin2).

Input   data/city_month_counts.csv – one row per county and complete calendar month,
        months without events included as 0 (derived, aggregated data; no raw ACLED records).
        Re-create it from a local ACLED extract with:  python run_evaluation.py --from-geojson PATH
Output  results.md, results.json

Protocol
  Rolling one-step-ahead: every month t in the test window is predicted from observed months < t
  only. All methods see the same information. Learned models (Random Forest, Poisson GLM) are
  fitted once on the months before the test window, using the same lag features.
  Primary window: the last 12 complete months; sensitivity: 6 and 24 months.
  A second, "events-only" protocol (rows with >= 1 event, lags taken from the previous row with
  events, baselines limited to training months) is reported to show how evaluation design alone
  changes the ranking.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

HERE = Path(__file__).resolve().parent
COUNTS = HERE / "data" / "city_month_counts.csv"
RESULTS_MD = HERE / "results.md"
RESULTS_JSON = HERE / "results.json"
SEED = 42
WINDOWS = (12, 6, 24)
N_BOOT = 2000


# ----------------------------------------------------------------------------- data
def counts_from_geojson(path: Path) -> pd.DataFrame:
    """County x month counts from a local ACLED GeoJSON extract; only complete months are kept."""
    with open(path, encoding="utf-8") as f:
        feats = json.load(f)["features"]
    rows, max_ts = [], 0
    for ft in feats:
        p = ft.get("properties") or {}
        date = str(p.get("event_date") or "")[:10]
        if len(date) != 10:
            continue
        city = p.get("admin2") or p.get("admin1") or "Unknown"
        rows.append((city, date[:7]))
        try:
            max_ts = max(max_ts, int(p.get("timestamp") or 0))
        except (TypeError, ValueError):
            pass
    ev = pd.DataFrame(rows, columns=["city", "ym"])
    last_date = max(r[1] for r in rows)
    extract = pd.Timestamp(max_ts, unit="s") if max_ts else pd.Timestamp(last_date + "-01") + pd.offsets.MonthEnd(0)
    last_complete = extract.to_period("M") if extract.is_month_end else extract.to_period("M") - 1
    months = pd.period_range(ev.ym.min(), last_complete, freq="M").strftime("%Y-%m")
    grid = ev[ev.ym.isin(months)].groupby(["city", "ym"]).size().unstack(fill_value=0)
    grid = grid.reindex(index=sorted(ev.city.unique()), columns=months, fill_value=0)
    grid.index.name, grid.columns.name = "city", "ym"
    out = grid.stack().rename("event_count").reset_index()
    out["year"] = out.ym.str[:4].astype(int)
    out["month"] = out.ym.str[5:].astype(int)
    return out[["city", "year", "month", "event_count"]]


def load_counts() -> pd.DataFrame:
    df = pd.read_csv(COUNTS)
    df["ym"] = df.year.astype(str) + "-" + df.month.astype(str).str.zfill(2)
    return df.sort_values(["city", "ym"]).reset_index(drop=True)


# ----------------------------------------------------------------------------- features
def with_features(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    g = df.groupby("city")["event_count"]
    for k in range(1, 13):
        df[f"lag{k}"] = g.shift(k)
    df["ma3"] = df[["lag1", "lag2", "lag3"]].mean(axis=1)
    df["ma6"] = df[[f"lag{k}" for k in range(1, 7)]].mean(axis=1)
    df["hist_mean"] = g.transform(lambda s: s.shift(1).expanding().mean())
    df["city_id"] = df.city.astype("category").cat.codes
    return df


FEATURES = ["city_id", "month", "lag1", "lag2", "lag3"]


def predictions(df: pd.DataFrame, test_months: list[str]) -> pd.DataFrame:
    """Predictions of every method for every county-month in the test window."""
    from sklearn.ensemble import RandomForestRegressor
    from sklearn.linear_model import PoissonRegressor

    train = df[(df.ym < test_months[0]) & df.lag3.notna()]
    test = df[df.ym.isin(test_months)].copy()
    rf = RandomForestRegressor(n_estimators=300, max_depth=8, min_samples_leaf=5, random_state=SEED, n_jobs=-1)
    rf.fit(train[FEATURES], train.event_count)
    lagf = ["lag1", "lag2", "lag3"]
    glm = PoissonRegressor(alpha=1e-4, max_iter=1000)
    glm.fit(np.log1p(train[lagf]), train.event_count)

    test["Always zero"] = 0.0
    test["Last month (naive)"] = test.lag1
    test["Same month last year"] = test.lag12
    test["3-month moving average"] = test.ma3
    test["6-month moving average"] = test.ma6
    test["Historical mean"] = test.hist_mean
    test["Poisson GLM (lags)"] = glm.predict(np.log1p(test[lagf]))
    test["Random Forest (lags + month)"] = np.maximum(rf.predict(test[FEATURES]), 0)
    return test


METHODS = ["Always zero", "Last month (naive)", "Same month last year", "Historical mean",
           "3-month moving average", "6-month moving average", "Poisson GLM (lags)", "Random Forest (lags + month)"]


def poisson_deviance(y, mu):
    mu = np.maximum(mu, 1e-6)
    with np.errstate(divide="ignore", invalid="ignore"):
        t = np.where(y > 0, y * np.log(y / mu), 0.0)
    return float(np.mean(2 * (t - (y - mu))))


def scores(test: pd.DataFrame) -> list[dict]:
    y = test.event_count.to_numpy(float)
    active = y > 0
    out = []
    for m in METHODS:
        p = test[m].to_numpy(float)
        e = y - p
        out.append({
            "method": m,
            "mae": float(np.mean(np.abs(e))),
            "rmse": float(np.sqrt(np.mean(e ** 2))),
            "mae_active": float(np.mean(np.abs(e[active]))),
            "deviance": poisson_deviance(y, p) if m != "Always zero" else None,
        })
    return out


def bootstrap_rmse_diff(test: pd.DataFrame, a: str, b: str, n: int = N_BOOT) -> dict:
    """County-cluster bootstrap of RMSE(a) - RMSE(b); negative favours a."""
    rng = np.random.default_rng(SEED)
    cities = test.city.unique()
    sq = {m: (test.event_count - test[m]) ** 2 for m in (a, b)}
    by_city = {m: sq[m].groupby(test.city).agg(["sum", "count"]) for m in (a, b)}
    diffs = []
    for _ in range(n):
        pick = rng.choice(cities, size=len(cities), replace=True)
        r = [np.sqrt(by_city[m].loc[pick, "sum"].sum() / by_city[m].loc[pick, "count"].sum()) for m in (a, b)]
        diffs.append(r[0] - r[1])
    lo, hi = np.percentile(diffs, [2.5, 97.5])
    full = [np.sqrt(sq[m].mean()) for m in (a, b)]
    return {"a": a, "b": b, "diff": float(full[0] - full[1]), "ci95": [float(lo), float(hi)]}


# ----------------------------------------------------------------------------- events-only protocol
def events_only_protocol(df: pd.DataFrame, test_months: list[str]) -> list[dict]:
    """
    Rows restricted to county-months with events. Baselines look up earlier months in the
    training rows only (so inside the test window they often find nothing and predict 0),
    while the Random Forest's lags are the previous rows *with events*, read from the test rows.
    """
    from sklearn.ensemble import RandomForestRegressor

    nz = df[df.event_count > 0][["city", "year", "month", "ym", "event_count"]].copy()
    train, test = nz[nz.ym < test_months[0]].copy(), nz[nz.ym.isin(test_months)].copy()
    lookup = {(r.city, r.ym): r.event_count for r in train.itertuples()}

    def prev(ym: str, k: int) -> str:
        return str(pd.Period(ym, "M") - k)

    test["ma3"] = [np.mean([lookup[(c, prev(m, k))] for k in (1, 2, 3) if (c, prev(m, k)) in lookup] or [0])
                   for c, m in zip(test.city, test.ym)]
    test["naive"] = [lookup.get((c, prev(m, 1)), 0) for c, m in zip(test.city, test.ym)]

    def lags(d):
        d = d.sort_values(["city", "ym"]).copy()
        g = d.groupby("city")["event_count"]
        for k in (1, 2, 3):
            d[f"lag{k}"] = g.shift(k).fillna(0)
        return d

    cats = pd.Categorical(nz.city).categories
    tr, te = lags(train), lags(test)
    for d in (tr, te):
        d["city_id"] = pd.Categorical(d.city, categories=cats).codes
    rf = RandomForestRegressor(n_estimators=50, max_depth=8, random_state=SEED).fit(tr[FEATURES], tr.event_count)
    te["rf"] = np.maximum(rf.predict(te[FEATURES]), 0)
    test = test.merge(te[["city", "ym", "rf"]], on=["city", "ym"])
    y = test.event_count.to_numpy(float)
    return [{"method": n, "mae": float(np.mean(np.abs(y - test[c]))), "rmse": float(np.sqrt(np.mean((y - test[c]) ** 2)))}
            for n, c in [("3-month moving average", "ma3"), ("Last month (naive)", "naive"), ("Random Forest (lags + month)", "rf")]]


# ----------------------------------------------------------------------------- main
def main() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--from-geojson", type=Path, help="rebuild data/city_month_counts.csv from a local ACLED GeoJSON extract")
    args = ap.parse_args()

    if args.from_geojson:
        counts = counts_from_geojson(args.from_geojson)
        COUNTS.parent.mkdir(exist_ok=True)
        counts.to_csv(COUNTS, index=False)
        print(f"wrote {len(counts)} county-months to {COUNTS}")
    if not COUNTS.exists():
        sys.exit(f"{COUNTS} not found; run with --from-geojson PATH")

    df = with_features(load_counts())
    months = sorted(df.ym.unique())
    res = {
        "data": {
            "counties": int(df.city.nunique()), "first_month": months[0], "last_month": months[-1],
            "months": len(months), "county_months": int(len(df)),
            "events": int(df.event_count.sum()), "share_zero": float((df.event_count == 0).mean()),
        },
        "windows": {},
    }
    for w in WINDOWS:
        test_months = months[-w:]
        test = predictions(df, test_months)
        entry = {
            "test_months": [test_months[0], test_months[-1]],
            "rows": int(len(test)), "active_rows": int((test.event_count > 0).sum()),
            "scores": scores(test),
        }
        if w == WINDOWS[0]:
            entry["bootstrap"] = [
                bootstrap_rmse_diff(test, "3-month moving average", "Random Forest (lags + month)"),
                bootstrap_rmse_diff(test, "3-month moving average", "Last month (naive)"),
                bootstrap_rmse_diff(test, "3-month moving average", "Poisson GLM (lags)"),
            ]
            entry["events_only_protocol"] = events_only_protocol(df, test_months)
            by_size = test.assign(size=pd.cut(test.hist_mean, [-0.01, 0.5, 2, 1e9], labels=["<0.5", "0.5-2", ">2"]))
            entry["by_activity"] = [
                {"group": str(k), "rows": int(len(g)),
                 **{m: float(np.sqrt(np.mean((g.event_count - g[m]) ** 2))) for m in ("3-month moving average", "Random Forest (lags + month)", "Last month (naive)")}}
                for k, g in by_size.groupby("size", observed=True)]
        res["windows"][str(w)] = entry

    RESULTS_JSON.write_text(json.dumps(res, indent=2), encoding="utf-8")
    write_markdown(res)
    print(RESULTS_MD.read_text(encoding="utf-8"))


def write_markdown(res: dict) -> None:
    d = res["data"]
    L = ["# Evaluation results", "",
         f"Data: {d['counties']} counties x {d['months']} complete months ({d['first_month']} to {d['last_month']}), "
         f"{d['events']:,} events, {d['share_zero']:.1%} of county-months without events.", "",
         "Protocol: rolling one-step-ahead; each month is predicted from earlier months only; all methods see the same history.", ""]
    for w, e in res["windows"].items():
        L += [f"## Test window: last {w} months ({e['test_months'][0]} to {e['test_months'][1]}; "
              f"{e['rows']:,} county-months, {e['active_rows']:,} with events)", "",
              "| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |", "|---|---:|---:|---:|---:|"]
        for s in sorted(e["scores"], key=lambda s: s["rmse"]):
            dev = "–" if s["deviance"] is None else f"{s['deviance']:.3f}"
            L.append(f"| {s['method']} | {s['mae']:.3f} | {s['rmse']:.3f} | {s['mae_active']:.3f} | {dev} |")
        L.append("")
        if "bootstrap" in e:
            L += ["County-cluster bootstrap (2,000 resamples), RMSE difference (negative favours the first method):", ""]
            for b in e["bootstrap"]:
                L.append(f"- {b['a']} − {b['b']}: {b['diff']:+.3f} (95% CI {b['ci95'][0]:+.3f} to {b['ci95'][1]:+.3f})")
            L += ["", "RMSE by county activity (historical mean events per month):", "",
                  "| Group | County-months | 3-month MA | Random Forest | Naive |", "|---|---:|---:|---:|---:|"]
            for g in e["by_activity"]:
                L.append(f"| {g['group']} | {g['rows']:,} | {g['3-month moving average']:.3f} | {g['Random Forest (lags + month)']:.3f} | {g['Last month (naive)']:.3f} |")
            L += ["", "Events-only protocol (rows with events only; baselines restricted to training months):", "",
                  "| Method | MAE | RMSE |", "|---|---:|---:|"]
            for s in e["events_only_protocol"]:
                L.append(f"| {s['method']} | {s['mae']:.3f} | {s['rmse']:.3f} |")
            L.append("")
    RESULTS_MD.write_text("\n".join(L) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
