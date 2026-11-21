#!/usr/bin/env python3
"""
Creates the app's local event file (CrisisZonesApplication/App_Data/conflict_data.geojson)
from ACLED data. ACLED's terms do not allow raw events to be redistributed, so each user
downloads them with their own ACLED account.

Two ways in:

1. ACLED API (needs an account with API access):
     set ACLED_EMAIL and ACLED_PASSWORD in the environment, then
     python tools/fetch_acled.py --api

2. CSV export from ACLED's Data Export Tool (Poland, 2020-01-01 to 2024-12-31):
     python tools/fetch_acled.py --csv path/to/export.csv

Credentials are read from the environment only; they are never written to disk.
The study period is fixed to 2020-01-01..2024-12-31 by default (--start/--end to change).
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "CrisisZonesApplication" / "App_Data" / "conflict_data.geojson"
TOKEN_URL = "https://acleddata.com/oauth/token"
READ_URL = "https://acleddata.com/api/acled/read"
UA = "crisis-zones-research/1.0"

FIELDS = ["event_id_cnty", "event_date", "year", "time_precision", "disorder_type", "event_type", "sub_event_type",
          "actor1", "assoc_actor_1", "inter1", "actor2", "assoc_actor_2", "inter2", "interaction", "civilian_targeting",
          "iso", "region", "country", "admin1", "admin2", "admin3", "location", "latitude", "longitude",
          "geo_precision", "source", "source_scale", "notes", "fatalities", "tags", "timestamp"]
NUMERIC = {"year", "time_precision", "iso", "geo_precision", "fatalities", "timestamp"}


DATE_FORMATS = ("%Y-%m-%d", "%d %B %Y", "%d-%B-%Y", "%d/%m/%Y", "%Y/%m/%d")


def iso_date(value) -> str | None:
    """ACLED dates as yyyy-mm-dd. API data are ISO; some website exports write '13 December 2024'."""
    text = str(value or "").strip()
    if not text:
        return None
    for fmt in DATE_FORMATS:
        try:
            return datetime.strptime(text[:10] if fmt == "%Y-%m-%d" else text, fmt).strftime("%Y-%m-%d")
        except ValueError:
            continue
    return None


def _post_form(url: str, data: dict) -> dict:
    req = urllib.request.Request(url, data=urllib.parse.urlencode(data).encode(), headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def fetch_api(country: str, start: str, end: str) -> list[dict]:
    email, password = os.environ.get("ACLED_EMAIL"), os.environ.get("ACLED_PASSWORD")
    if not email or not password:
        sys.exit("Set ACLED_EMAIL and ACLED_PASSWORD in the environment (API access must be enabled on your ACLED account).")
    try:
        token = _post_form(TOKEN_URL, {"username": email, "password": password, "grant_type": "password",
                                       "client_id": "acled", "scope": "authenticated"})["access_token"]
    except urllib.error.HTTPError as e:
        sys.exit(f"ACLED login failed (HTTP {e.code}). Check ACLED_EMAIL and ACLED_PASSWORD.")
    except (urllib.error.URLError, KeyError, ValueError) as e:
        sys.exit(f"Could not obtain an ACLED access token: {e}")
    rows, page, limit = [], 1, 5000
    while True:
        q = urllib.parse.urlencode({"_format": "json", "country": country, "event_date": f"{start}|{end}",
                                    "event_date_where": "BETWEEN", "fields": "|".join(FIELDS), "limit": limit, "page": page})
        req = urllib.request.Request(f"{READ_URL}?{q}", headers={"Authorization": f"Bearer {token}", "User-Agent": UA})
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                body = json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 403:
                sys.exit("ACLED returned 403: API access is not enabled for this account. Use --csv with an export instead.")
            sys.exit(f"ACLED API request failed (HTTP {e.code}).")
        except urllib.error.URLError as e:
            sys.exit(f"Could not reach the ACLED API: {e.reason}")
        if body.get("success") is False or body.get("error"):
            sys.exit(f"ACLED API error: {body.get('error') or body.get('message') or body}")
        batch = body.get("data") or []
        rows += batch
        print(f"  page {page}: {len(batch)} events")
        if len(batch) < limit:
            return rows
        page += 1


def read_csv(path: Path, start: str, end: str) -> list[dict]:
    if not path.exists():
        sys.exit(f"CSV file not found: {path}")
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        missing = {"event_id_cnty", "event_date", "latitude", "longitude"} - set(reader.fieldnames or [])
        if missing:
            sys.exit(f"{path.name} is not an ACLED export: missing column(s) {', '.join(sorted(missing))}")
        rows = []
        for r in reader:
            d = iso_date(r.get("event_date"))
            if d and start <= d <= end:
                r["event_date"] = d
                rows.append(r)
        return rows


def to_feature(r: dict) -> dict | None:
    try:
        lat, lng = float(r["latitude"]), float(r["longitude"])
    except (KeyError, TypeError, ValueError):
        return None
    date = iso_date(r.get("event_date"))
    if not (-90 <= lat <= 90 and -180 <= lng <= 180) or (lat == 0 and lng == 0) or not r.get("event_id_cnty") or not date:
        return None
    r = {**r, "event_date": date}
    props = {}
    for k in FIELDS:
        v = r.get(k, "")
        if isinstance(v, str):
            v = v.strip()
        if k in NUMERIC:
            try:
                v = int(float(v)) if v not in ("", None) else 0
            except ValueError:
                v = 0
        elif k in ("latitude", "longitude"):
            v = float(v)
        else:
            v = "" if v is None else str(v)
        props[k] = v
    return {"type": "Feature", "geometry": {"type": "Point", "coordinates": [lng, lat]}, "properties": props}


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--api", action="store_true", help="download with the ACLED API")
    src.add_argument("--csv", type=Path, help="convert an ACLED CSV export")
    ap.add_argument("--country", default="Poland")
    ap.add_argument("--start", default="2020-01-01")
    ap.add_argument("--end", default="2024-12-31")
    ap.add_argument("--out", type=Path, default=OUT)
    a = ap.parse_args()

    if not (iso_date(a.start) == a.start and iso_date(a.end) == a.end and a.start <= a.end):
        sys.exit("--start and --end must be dates as yyyy-mm-dd, start before end")
    rows = fetch_api(a.country, a.start, a.end) if a.api else read_csv(a.csv, a.start, a.end)
    if not rows:
        sys.exit("No events found for this country and period; nothing written.")
    feats = [f for f in map(to_feature, rows) if f]
    feats.sort(key=lambda f: (f["properties"]["event_date"], f["properties"]["event_id_cnty"]))
    a.out.parent.mkdir(parents=True, exist_ok=True)
    data = json.dumps({"type": "FeatureCollection", "features": feats}, ensure_ascii=False).encode("utf-8")
    a.out.write_bytes(data)
    print(f"{len(feats)} events written to {a.out} ({len(rows) - len(feats)} without usable coordinates skipped)")
    print(f"sha256 {hashlib.sha256(data).hexdigest()}")
    print("ACLED revises records over time, so counts can differ slightly from the published analysis.")
    print("Next: python evaluation/run_evaluation.py --from-geojson", a.out)


if __name__ == "__main__":
    main()
