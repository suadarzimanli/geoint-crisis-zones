#!/usr/bin/env python3
"""Figures for the paper from data/city_month_counts.csv and results.json (run run_evaluation.py first)."""
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import pandas as pd

HERE = Path(__file__).resolve().parent
OUT = HERE.parent / "paper" / "figures"
OUT.mkdir(parents=True, exist_ok=True)

# Reference palette (light): series-1 blue, series-2 orange; neutral grey for context marks
BLUE, ORANGE, GREY = "#2a78d6", "#eb6834", "#b9b8b3"
INK, INK2, GRID = "#0b0b0b", "#52514e", "#e6e5e0"
plt.rcParams.update({
    "font.family": "DejaVu Sans", "font.size": 9, "axes.edgecolor": INK2, "axes.labelcolor": INK2,
    "xtick.color": INK2, "ytick.color": INK2, "axes.spines.top": False, "axes.spines.right": False,
    "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.8, "axes.axisbelow": True,
    "savefig.bbox": "tight", "savefig.dpi": 300,
})

counts = pd.read_csv(HERE / "data" / "city_month_counts.csv")
res = json.loads((HERE / "results.json").read_text(encoding="utf-8"))

# 1 National monthly events
s = counts.groupby(["year", "month"]).event_count.sum().reset_index()
s["date"] = pd.to_datetime(dict(year=s.year, month=s.month, day=1))
fig, ax = plt.subplots(figsize=(6.3, 2.4))
ax.plot(s.date, s.event_count, color=BLUE, lw=1.8)
ax.set_ylabel("Events per month")
ax.set_ylim(0, 1000)
ax.grid(axis="x", visible=False)
for (y, m), text, dx in [((2020, 10), "Constitutional Tribunal abortion\nruling protests (Oct 2020)", 60),
                         ((2024, 2), "Farmers' protests\n(Feb 2024)", -330)]:
    row = s[(s.year == y) & (s.month == m)].iloc[0]
    ax.annotate(f"{text}: {row.event_count}", (row.date, row.event_count),
                xytext=(row.date + pd.Timedelta(days=dx), row.event_count - (60 if y == 2020 else -180)),
                fontsize=8, color=INK, arrowprops=dict(arrowstyle="-", color=INK2, lw=0.7))
ax.margins(x=0.01)
import matplotlib.dates as mdates
ax.xaxis.set_major_locator(mdates.YearLocator())
ax.xaxis.set_major_formatter(mdates.DateFormatter("%Y"))
fig.savefig(OUT / "fig-monthly.pdf")
plt.close(fig)

# 2 RMSE by method, 12-month window (highlight the method shown in the app)
sc = sorted(res["windows"]["12"]["scores"], key=lambda r: r["rmse"], reverse=True)
fig, ax = plt.subplots(figsize=(6.3, 2.6))
names = [r["method"] for r in sc]
vals = [r["rmse"] for r in sc]
colors = [BLUE if n == "3-month moving average" else GREY for n in names]
ax.barh(names, vals, color=colors, height=0.55)
for i, v in enumerate(vals):
    ax.text(v + 0.02, i, f"{v:.2f}", va="center", fontsize=8, color=INK)
ax.set_xlabel("RMSE (events per county-month), Dec 2023 – Nov 2024")
ax.set_xlim(0, max(vals) * 1.12)
ax.grid(axis="y", visible=False)
fig.savefig(OUT / "fig-rmse.pdf")
plt.close(fig)

# 3 Same methods, two evaluation designs (MAE)
fair = {r["method"]: r["mae_active"] for r in res["windows"]["12"]["scores"]}
eo = {r["method"]: r["mae"] for r in res["windows"]["12"]["events_only_protocol"]}
methods = ["Last month (naive)", "3-month moving average", "Random Forest (lags + month)"]
labels = ["Last month", "3-month MA", "Random Forest"]
fig, ax = plt.subplots(figsize=(6.3, 2.3))
y = range(len(methods))
ax.barh([i + 0.18 for i in y], [eo[m] for m in methods], height=0.34, color=ORANGE, label="Events-only design")
ax.barh([i - 0.18 for i in y], [fair[m] for m in methods], height=0.34, color=BLUE, label="Same-information design")
for i, m in enumerate(methods):
    ax.text(eo[m] + 0.03, i + 0.18, f"{eo[m]:.2f}", va="center", fontsize=8, color=INK)
    ax.text(fair[m] + 0.03, i - 0.18, f"{fair[m]:.2f}", va="center", fontsize=8, color=INK)
ax.set_yticks(list(y), labels)
ax.set_xlabel("MAE on county-months with events")
ax.set_xlim(0, 3.0)
ax.grid(axis="y", visible=False)
ax.legend(frameon=False, loc="lower center", bbox_to_anchor=(0.5, 1.0), ncol=2, fontsize=8)
fig.savefig(OUT / "fig-protocol.pdf")
plt.close(fig)
print("figures written to", OUT)
