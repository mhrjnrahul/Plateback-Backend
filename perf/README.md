# Performance profiling (Week 5)

How the API was load-tested and profiled, and the raw results. The findings and fixes are written up in the frontend repository's [docs/testing.md](https://github.com/mhrjnrahul/PlateBack---Frontend/blob/main/docs/testing.md#performance).

| File | What it is |
| --- | --- |
| `load-data.sql` | 400 extra businesses, 30,000 listings (about 6,000 live) and two years of history for Sunrise Bakery, so profiling sees realistic volume. Deterministic (`setseed`). |
| `load-test.mjs` | 10 scenarios with [autocannon](https://github.com/mcollina/autocannon): 3 s warm-up, then 15 s at 20 connections each. Writes `results/<label>.md`. |
| `results/before.md`, `results/after.md` | The Week 4 code (`5a0e182`) and the Week 5 code, run back to back on the same data |
| `results/counters-before.csv`, `results/counters-after.csv` | `dotnet-counters` (CPU, allocations, GC) during the 25 km search, before and after |

## Running it

```bash
# 1. Demo data plus the load data (the seed removes the load data again)
dotnet run --project src/PlateBack.Api -- seed
psql -h localhost -U plateback -d plateback -f perf/load-data.sql

# 2. A Release build of the API, with a high login limit for the test's setup logins
dotnet build -c Release
RateLimits__AuthPerMinute=100000 dotnet run -c Release --no-build --project src/PlateBack.Api

# 3. The load test (another terminal)
cd perf && npm ci && node load-test.mjs my-run      # ONLY="Search 25" runs one scenario
```

Profilers (pinned in `dotnet-tools.json`, `dotnet tool restore` installs them):

```bash
dotnet tool run dotnet-counters collect -p <pid> --counters System.Runtime --duration 00:00:00:15 --format csv -o counters.csv
dotnet tool run dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time --duration 00:00:00:10 --format speedscope
```

Database plans: `EXPLAIN (ANALYZE, BUFFERS)` on the SQL that EF Core logs when started with `Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command=Information`. Index use: `SELECT indexrelname, idx_scan FROM pg_stat_user_indexes WHERE relname = 'listings';`.
