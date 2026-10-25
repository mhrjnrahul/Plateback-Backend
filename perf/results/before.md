# Load test: before

2026-10-25T05:26:31.227Z, 20 connections, 3 s warm-up then 15 s per scenario, API at http://localhost:5080

| Scenario | Requests/s | p50 (ms) | p99 (ms) | KB per response | Errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| Search 5 km, by distance (browse default) | 220 | 83 | 210 | 8.5 | 0 |
| Search 25 km, by distance (largest radius) | 54 | 351 | 685 | 8.5 | 0 |
| Search 5 km, page 10 | 207 | 91 | 179 | 8.6 | 0 |
| Search without location, by pickup time | 427 | 44 | 89 | 8.6 | 0 |
| Search text "momo" within 5 km | 491 | 38 | 79 | 8.5 | 0 |
| Listing detail | 4363 | 4 | 9 | 0.9 | 0 |
| Impact stats (cached) | 18770 | 1 | 2 | 0.3 | 0 |
| My reservations (signed in) | 2176 | 8 | 17 | 1.6 | 0 |
| Business dashboard stats | 1438 | 13 | 22 | 0.8 | 0 |
| Business listings table | 87 | 224 | 377 | 20.8 | 0 |
