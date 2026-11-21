# Local event data

This folder holds `conflict_data.geojson`, the ACLED extract the app reads. It is not part of the repository because ACLED's terms do not allow raw events to be redistributed.

Create it with `python tools/fetch_acled.py --api` (ACLED API access) or `--csv export.csv` (export from ACLED's website). See the main README.
