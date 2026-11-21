# Evaluation results

Data: 351 counties x 59 complete months (2020-01 to 2024-11), 7,756 events, 85.5% of county-months without events.

Protocol: rolling one-step-ahead; each month is predicted from earlier months only; all methods see the same history.

## Test window: last 12 months (2023-12 to 2024-11; 4,212 county-months, 623 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Random Forest (lags + month) | 0.492 | 1.336 | 1.935 | 1.329 |
| 3-month moving average | 0.472 | 1.409 | 1.913 | 3.558 |
| Last month (naive) | 0.433 | 1.419 | 1.936 | 4.325 |
| Poisson GLM (lags) | 0.500 | 1.449 | 2.008 | 1.189 |
| 6-month moving average | 0.512 | 1.501 | 1.937 | 3.314 |
| Historical mean | 0.497 | 1.518 | 2.047 | 1.618 |
| Same month last year | 0.422 | 1.698 | 2.189 | 7.046 |
| Always zero | 0.407 | 1.823 | 2.753 | – |

County-cluster bootstrap (2,000 resamples), RMSE difference (negative favours the first method):

- 3-month moving average − Random Forest (lags + month): +0.073 (95% CI -0.081 to +0.226)
- 3-month moving average − Last month (naive): -0.010 (95% CI -0.185 to +0.177)
- 3-month moving average − Poisson GLM (lags): -0.040 (95% CI -0.133 to +0.066)

RMSE by county activity (historical mean events per month):

| Group | County-months | 3-month MA | Random Forest | Naive |
|---|---:|---:|---:|---:|
| <0.5 | 3,702 | 0.965 | 0.921 | 1.018 |
| 0.5-2 | 319 | 3.248 | 2.955 | 3.039 |
| >2 | 191 | 2.843 | 2.889 | 2.975 |

Events-only protocol (rows with events only; baselines restricted to training months):

| Method | MAE | RMSE |
|---|---:|---:|
| 3-month moving average | 2.401 | 3.790 |
| Last month (naive) | 2.583 | 4.167 |
| Random Forest (lags + month) | 1.704 | 3.685 |

## Test window: last 6 months (2024-06 to 2024-11; 2,106 county-months, 135 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Poisson GLM (lags) | 0.291 | 0.653 | 1.308 | 0.571 |
| 3-month moving average | 0.197 | 0.706 | 1.217 | 0.909 |
| Random Forest (lags + month) | 0.295 | 0.721 | 1.295 | 0.599 |
| Historical mean | 0.310 | 0.738 | 1.288 | 0.573 |
| Last month (naive) | 0.155 | 0.784 | 1.437 | 1.395 |
| 6-month moving average | 0.355 | 0.952 | 1.184 | 0.891 |
| Always zero | 0.156 | 1.109 | 2.430 | – |
| Same month last year | 0.200 | 1.163 | 1.415 | 1.365 |

## Test window: last 24 months (2022-12 to 2024-11; 8,424 county-months, 1,047 with events)

| Method | MAE | RMSE | MAE (months with events) | Poisson deviance |
|---|---:|---:|---:|---:|
| Random Forest (lags + month) | 0.410 | 1.156 | 1.731 | 1.073 |
| 3-month moving average | 0.346 | 1.169 | 1.690 | 2.706 |
| Poisson GLM (lags) | 0.429 | 1.182 | 1.740 | 0.969 |
| Last month (naive) | 0.330 | 1.202 | 1.763 | 3.481 |
| 6-month moving average | 0.366 | 1.215 | 1.676 | 2.377 |
| Historical mean | 0.416 | 1.293 | 1.858 | 1.195 |
| Always zero | 0.314 | 1.522 | 2.528 | – |
| Same month last year | 0.382 | 1.643 | 2.122 | 4.884 |

