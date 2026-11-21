# Evaluation results

Data: 341 counties x 59 complete months (2020-01 to 2024-11), 7,756 events, 85.2% of county-months without events.

Protocol: rolling one-step-ahead; each month is predicted from earlier months only; all methods see the same history.

## Test window: last 12 months (2023-12 to 2024-11; 4,092 county-months, 619 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Random Forest (lags + month) | 0.504 | 1.351 | 1.915 | 1.354 |
| 3-month moving average | 0.483 | 1.426 | 1.914 | 3.608 |
| Last month (naive) | 0.441 | 1.434 | 1.932 | 4.402 |
| Poisson GLM (lags) | 0.512 | 1.462 | 2.001 | 1.209 |
| 6-month moving average | 0.525 | 1.523 | 1.938 | 3.375 |
| Historical mean | 0.510 | 1.541 | 2.056 | 1.641 |
| Same month last year | 0.434 | 1.726 | 2.204 | 7.258 |
| Always zero | 0.419 | 1.852 | 2.771 | – |

County-cluster bootstrap (2,000 resamples), RMSE difference (negative favours the first method):

- 3-month moving average − Random Forest (lags + month): +0.074 (95% CI -0.082 to +0.234)
- 3-month moving average − Last month (naive): -0.008 (95% CI -0.178 to +0.169)
- 3-month moving average − Poisson GLM (lags): -0.037 (95% CI -0.129 to +0.069)

RMSE by county activity (historical mean events per month):

| Group | County-months | 3-month MA | Random Forest | Naive |
|---|---:|---:|---:|---:|
| <0.5 | 3,566 | 0.976 | 0.937 | 1.028 |
| 0.5-2 | 334 | 3.167 | 2.866 | 2.972 |
| >2 | 192 | 2.859 | 2.884 | 2.970 |

Events-only protocol (rows with events only; baselines restricted to training months):

| Method | MAE | RMSE |
|---|---:|---:|
| 3-month moving average | 2.415 | 3.810 |
| Last month (naive) | 2.599 | 4.188 |
| Random Forest (lags + month) | 1.680 | 3.625 |

## Test window: last 6 months (2024-06 to 2024-11; 2,046 county-months, 135 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Poisson GLM (lags) | 0.298 | 0.662 | 1.300 | 0.583 |
| 3-month moving average | 0.203 | 0.718 | 1.222 | 0.935 |
| Random Forest (lags + month) | 0.301 | 0.747 | 1.304 | 0.615 |
| Historical mean | 0.319 | 0.749 | 1.286 | 0.589 |
| Last month (naive) | 0.159 | 0.796 | 1.437 | 1.436 |
| 6-month moving average | 0.365 | 0.972 | 1.185 | 0.916 |
| Always zero | 0.160 | 1.125 | 2.430 | – |
| Same month last year | 0.206 | 1.180 | 1.415 | 1.405 |

## Test window: last 24 months (2022-12 to 2024-11; 8,184 county-months, 1,043 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Random Forest (lags + month) | 0.420 | 1.170 | 1.730 | 1.087 |
| 3-month moving average | 0.354 | 1.184 | 1.687 | 2.752 |
| Poisson GLM (lags) | 0.439 | 1.195 | 1.730 | 0.985 |
| Last month (naive) | 0.337 | 1.216 | 1.758 | 3.552 |
| 6-month moving average | 0.375 | 1.233 | 1.674 | 2.348 |
| Historical mean | 0.427 | 1.312 | 1.861 | 1.201 |
| Always zero | 0.323 | 1.545 | 2.538 | – |
| Same month last year | 0.394 | 1.668 | 2.130 | 5.029 |

