# Load test: after

2026-10-25T05:29:44.662Z, 20 connections, 3 s warm-up then 15 s per scenario, API at http://localhost:5080

| Scenario | Requests/s | p50 (ms) | p99 (ms) | KB per response | Errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| Search 5 km, by distance (browse default) | 601 | 32 | 56 | 8.6 | 0 |
| Search 25 km, by distance (largest radius) | 342 | 57 | 87 | 8.6 | 0 |
| Search 5 km, page 10 | 610 | 31 | 50 | 8.6 | 0 |
| Search without location, by pickup time | 685 | 28 | 45 | 8.6 | 0 |
| Search text "momo" within 5 km | 490 | 39 | 63 | 8.5 | 0 |
| Listing detail | 4306 | 4 | 9 | 0.9 | 0 |
| Impact stats (cached) | 27396 | 0 | 2 | 0.3 | 0 |
| My reservations (signed in) | 2106 | 9 | 15 | 1.6 | 0 |
| Business dashboard stats | 1357 | 14 | 23 | 0.8 | 0 |
| Business listings table | 785 | 24 | 40 | 20.8 | 0 |
