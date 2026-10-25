# PlateBack: backend

PlateBack lets bakeries, cafes and restaurants list end-of-day surplus food, and lets people nearby reserve it and collect it with a pickup code during a short window. This repository is the REST API: ASP.NET Core on .NET 10 with PostgreSQL, built in Week 3 of the YuvaIntern programme from the Week 1 design document, and connected to the [React frontend](https://github.com/mhrjnrahul/PlateBack---Frontend) in Week 4.

- **API reference:** [docs/API.md](docs/API.md), plus the OpenAPI document [docs/openapi.json](docs/openapi.json) and an interactive page at `/scalar` while the API runs.
- **Design decisions:** [docs/decisions.md](docs/decisions.md)
- **Running the whole app (frontend and backend together):** see the [frontend repository's README](https://github.com/mhrjnrahul/PlateBack---Frontend#readme).
- **Week 3 submission:** the ZIP of this repository is attached to the [week3 release](https://github.com/mhrjnrahul/Plateback-Backend/releases/tag/week3).
- **Week 4 submission:** one ZIP with both projects is attached to the frontend's [week4 release](https://github.com/mhrjnrahul/PlateBack---Frontend/releases/tag/week4). What changed here is listed under [Week 4 changes](#week-4-changes).
- **Week 5 submission (testing, debugging, optimisation):** one ZIP with both projects, test reports and error logs is attached to the frontend's [week5 release](https://github.com/mhrjnrahul/PlateBack---Frontend/releases/tag/week5). The strategy, coverage, profiling and debugging reports are in the frontend repository's [docs/testing.md](https://github.com/mhrjnrahul/PlateBack---Frontend/blob/main/docs/testing.md) and [docs/debugging.md](https://github.com/mhrjnrahul/PlateBack---Frontend/blob/main/docs/debugging.md); what changed here is under [Week 5 changes](#week-5-changes).

## Contents

- [Week 3 brief checklist](#week-3-brief-checklist)
- [Week 4 changes](#week-4-changes)
- [Week 5 changes](#week-5-changes)
- [Run it locally](#run-it-locally)
- [Configuration](#configuration)
- [What the API does](#what-the-api-does)
- [Tech stack](#tech-stack)
- [Project structure](#project-structure)
- [Database](#database)
- [Security](#security)
- [Error handling and validation](#error-handling-and-validation)
- [Testing](#testing)
- [Development process](#development-process)
- [Known limitations](#known-limitations)

## Week 3 brief checklist

| The brief asks for | Where it is |
| --- | --- |
| RESTful APIs for the web application, with CRUD operations | 40 endpoints under `/api/v1` for accounts, businesses, listings, reservations, notifications and administration ([docs/API.md](docs/API.md)) |
| A back-end framework | ASP.NET Core minimal APIs on .NET 10 ([Tech stack](#tech-stack)) |
| A database and a schema that supports the application | PostgreSQL 16 through EF Core: 10 application tables plus Identity's, with the business rules as constraints ([Database](#database)) |
| Secure authentication | ASP.NET Core Identity, 15-minute JWTs, rotating refresh tokens in httpOnly cookies, lockout and rate limiting ([Security](#security)) |
| Error handling | Every error is RFC 9457 ProblemDetails with a stable `code` ([Error handling](#error-handling-and-validation)) |
| Data validation | FluentValidation on every request body and query, and domain rules in the entities |
| Tests | 181 tests: 55 domain, 24 application, 102 integration tests against real PostgreSQL ([Testing](#testing)) |
| API documentation: endpoints, methods, parameters and responses | [docs/API.md](docs/API.md), [docs/openapi.json](docs/openapi.json) and the `/scalar` page |
| A README with setup and execution instructions | This file ([Run it locally](#run-it-locally)) |
| About 30 to 35 hours | About 30 to 35 hours ([Development process](#development-process)) |

## Week 4 changes

Connecting the frontend showed problems that tests against the API alone could not. Each change below came from that, and the [integration write-up](https://github.com/mhrjnrahul/PlateBack---Frontend/blob/main/docs/integration.md) has the evidence for each.

| Change | Why |
| --- | --- |
| **SignalR hub at `/hubs/live`** ([docs/API.md](docs/API.md#live-updates-signalr), decision 9) | Stock, the counter queue and notifications reach open pages in about 0.2 seconds instead of on the next 15-second poll. |
| **One log entry per failed request** | A single database failure wrote 5 error entries with 4 copies of the stack trace. Now the request log is the only one, with the trace id the client sees. |
| **Cancelled requests answer 499** | React cancels requests it no longer needs; the API logged each one as a 500, and the impact-stats cache passed the cancellation on to other waiting requests. |
| **30-second grace window for rotated refresh tokens** (decision 10) | Two refreshes with the same cookie (React's double mount, two tabs, a reload mid-refresh) looked like a stolen token and signed people out. |
| **Upload URLs are site-relative** (`/uploads/...`) | Absolute URLs stored the API's own host (`localhost:5080`) in listings, which breaks behind a proxy or on another host. |

## Week 5 changes

Profiled with 30,000 listings (`perf/load-data.sql`), a load test, runtime counters, CPU traces and `EXPLAIN`; see [perf/README.md](perf/README.md) and decision 11.

| Change | Measured effect (Week 4 code to Week 5 code, same data) |
| --- | --- |
| **Listing search distance, sort, count and paging run in SQL**, and only the page's listings are loaded | 25 km search 54 to 342 requests/s (p99 685 to 87 ms); 21.6 to 0.37 MB allocated per request |
| **Covering index of live listings** (`ix_listings_live`), replacing a location index with 0 recorded scans | Count and page become index-only scans; 5 km search 220 to 601 requests/s |
| **A business's listings table pages in SQL** | 87 to 785 requests/s with two years of history (3,650 listings) |
| **18 new tests** for what the coverage report showed untested | Lines 84.1 to 88.0%, methods 90.1 to 95.6%; see [Testing](#testing) |
| **CI** (`.github/workflows/tests.yml`) | Build, style check and all tests against PostgreSQL 16 on every push, with coverage |

## Run it locally

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.112 or newer; see `global.json`)
- PostgreSQL 16 running on `localhost:5432`
- The EF Core tool, only if you want to run migrations by hand: `dotnet tool install --global dotnet-ef`

### Get the code

```bash
git clone https://github.com/mhrjnrahul/Plateback-Backend.git
cd Plateback-Backend
```

### 1. Create a database login

Once, as the PostgreSQL superuser:

```bash
sudo -u postgres psql -c "CREATE ROLE plateback LOGIN CREATEDB PASSWORD 'plateback';"
```

`CREATEDB` lets the API create its own databases (`plateback`, and `plateback_test` for the tests). Use any name and password you like; just put the same ones in the next step.

### 2. Store the secrets

Secrets never go in `appsettings.json`. For development they live in .NET user secrets (outside the repository):

```bash
cd src/PlateBack.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=plateback;Username=plateback;Password=plateback"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
cd ../..
```

The API refuses to start without both (the JWT key must be at least 32 characters).

### 3. Create the database and load the demo data

```bash
dotnet run --project src/PlateBack.Api -- seed
```

This applies the EF Core migrations (creating the database if needed) and loads the demo data. Run it again at any time to reset the demo data. The data uses times relative to now, so pickup windows are always open when you start.

### 4. Run the API

```bash
dotnet run --project src/PlateBack.Api
```

| URL | What |
| --- | --- |
| http://localhost:5080/scalar | Interactive API documentation; you can call endpoints from it |
| http://localhost:5080/openapi/v1.json | OpenAPI 3 document |
| http://localhost:5080/health | Health check, including the database |
| http://localhost:5080/api/v1/listings | Example: search listings |

Quick check from a terminal:

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"maya@example.com","password":"password123"}' | jq -r .accessToken)
curl -s http://localhost:5080/api/v1/users/me -H "Authorization: Bearer $TOKEN"
```

### Demo accounts

Every account uses the password **`password123`**.

| Role | Email | Try |
| --- | --- | --- |
| Customer | maya@example.com | Reserve, see pickup codes, cancel (holds code `K7M2-9QXA`) |
| Business owner | arjun@sunrisebakery.example | Sunrise Bakery: dashboard, publish listings, verify code `K7M2-9QXA` |
| Owner of a pending business | r.shrestha@mail.example | Hilltop Kitchen, waiting for review: cannot publish yet |
| Admin | admin@plateback.example | Verify, reject or suspend businesses and accounts; audit log |

The businesses are in Kathmandu, Lalitpur and Bhaktapur. Search around Thamel with `lat=27.7154&lng=85.3123`.

### Commands

| Command | What it does |
| --- | --- |
| `dotnet build` | Builds everything; warnings fail the build |
| `dotnet test` | Runs all 181 tests (PostgreSQL must be running) |
| `dotnet test -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml` | The same with code coverage; `dotnet tool restore` then `dotnet tool run reportgenerator` turns it into HTML (see `.github/workflows/tests.yml`) |
| `dotnet run --project src/PlateBack.Api` | Runs the API on http://localhost:5080 |
| `dotnet run --project src/PlateBack.Api -- seed` | Migrates the database and resets the demo data |
| `dotnet format --verify-no-changes` | Checks the code style from `.editorconfig` |
| `dotnet ef migrations add <Name> --project src/PlateBack.Infrastructure --startup-project src/PlateBack.Api --output-dir Persistence/Migrations` | Adds a migration after changing the model |

## Configuration

Settings come from `appsettings.json`, then user secrets (development), then environment variables (`Jwt__Key`, `ConnectionStrings__Default`, ...).

| Setting | Default | Purpose |
| --- | --- | --- |
| `ConnectionStrings:Default` | (secret) | PostgreSQL connection |
| `Jwt:Key` | (secret) | Signing key for access tokens, 32+ characters |
| `Jwt:Issuer`, `Jwt:Audience` | `plateback-api`, `plateback-web` | Checked on every token |
| `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays` | 15, 7 | Token lifetimes |
| `Jwt:RefreshReuseGraceSeconds` | 30 | How long a just-rotated refresh token still gets an access token (decision 10) |
| `Cors:Origins` | `http://localhost:5173` | Origins allowed to call the API directly. The frontend's dev server proxies requests, so it does not need this |
| `BusinessRules:*` | design doc 5.6 | Reservation limits, reliability changes, pickup window limits, search radius |
| `RateLimits:AuthPerMinute`, `RateLimits:PickupCodesPerMinute` | 20, 30 | Rate limits |
| `Uploads:Path` | `uploads` | Folder for listing photos |
| `Jobs:Enabled`, `Jobs:ExpiryIntervalSeconds`, `Jobs:OutboxIntervalSeconds` | true, 60, 10 | Background jobs |

## What the API does

The use cases from the design document, grouped as in [docs/API.md](docs/API.md):

- **Accounts:** register, log in, refresh and log out; read and edit your profile with your reliability score and business memberships.
- **Businesses:** register a business (starts Pending), edit it, manage staff, and see dashboard numbers (today and this month in the business's own time zone).
- **Listings:** search by distance, category, price, pickup time and text; publish, edit and cancel listings (verified businesses only).
- **Reservations:** reserve with a pickup code, see active and past reservations, cancel (late cancellations cost reliability), and hand food over at the counter by checking the code.
- **Notifications:** confirmations, cancellations, pickups, no-shows and review decisions, with an unread count.
- **Administration:** the verification queue, verify, reject or suspend businesses, suspend or restore accounts, remove listings, and read the audit log.
- **Background jobs:** every minute, listings past their window expire and uncollected reservations become no-shows (-20 reliability); every 10 seconds, the outbox is delivered as notifications.
- **Photo upload** for listings.
- **Live updates:** a SignalR hub tells open pages when stock, a business's queue or a user's notifications change, so they refetch at once.

## Tech stack

Versions are pinned in `Directory.Packages.props` (central package management).

| Area | Choice | Why |
| --- | --- | --- |
| Framework | ASP.NET Core 10, minimal APIs | Fast, typed, and endpoint groups map cleanly to the API catalogue |
| Database | PostgreSQL 16 with EF Core 10 and Npgsql | Constraints, partial indexes, row locks and jsonb; LINQ for most queries, migrations for the schema |
| Naming | EFCore.NamingConventions | snake_case tables and columns, readable in psql |
| Accounts | ASP.NET Core Identity | Tested password hashing (PBKDF2), lockout and unique emails |
| Tokens | JWT Bearer (HMAC-SHA256) | Short-lived access tokens for the single-page app |
| Validation | FluentValidation 12 | Request rules in one class per request, mirrored by the frontend's forms |
| Logging | Serilog | Structured logs with a correlation id on every line |
| API docs | Microsoft.AspNetCore.OpenApi and Scalar | OpenAPI 3 generated from the endpoints, with an interactive page |
| Live updates | ASP.NET Core SignalR | Part of the framework; WebSockets with automatic fallbacks, and the same JWT as the REST API |
| Tests | xUnit v3, Shouldly, WebApplicationFactory | Unit tests and in-memory API tests against real PostgreSQL |

MediatR and FluentAssertions, common choices for these jobs, were left out because of licence changes (decisions 1 and 2).

## Project structure

Clean Architecture, as in design doc 4.2: dependencies point inward, and the Domain project references nothing.

```
src/
  PlateBack.Domain/          entities with their rules, enums, error codes, rule values
  PlateBack.Application/     use cases: one handler class per endpoint, validators, DTOs,
    Common/                    interfaces for the database, identity, tokens and storage
    Features/<Area>/           Auth, Users, Organizations, Listings, Reservations,
                               Notifications, Admin, Stats, Uploads, Jobs
  PlateBack.Infrastructure/  EF Core context, mappings and migrations, Identity, JWT,
                             file storage, background job runner, demo data seeder
  PlateBack.Api/             Program.cs, endpoint groups, error handling, auth setup,
                             SignalR hub (Realtime/)
tests/
  PlateBack.Domain.Tests/          rules of the entities (no database)
  PlateBack.Application.Tests/     validators and helpers (no database)
  PlateBack.Api.IntegrationTests/  the real API in memory, against PostgreSQL
docs/
  API.md  openapi.json  decisions.md
```

**Patterns used:**

- **Rich domain model.** State changes are methods with rules inside them (`reservation.Cancel(now, pickupStart)`, `listing.Update(...)`); there are no public setters.
- **Use-case handlers.** One class per operation (`ReserveListingHandler`, `VerifyPickupHandler`), injected straight into the endpoint. No mediator (decision 1).
- **Ports and adapters.** The Application layer defines `IAppDbContext`, `IIdentityService`, `ITokenService`, `IImageStorage` and `ILiveUpdates`; Infrastructure and the Api project implement them. Handlers call `ILiveUpdates` after their transaction commits, so a page is never told about a change that was rolled back.
- **Transactional outbox.** Notifications are written in the same transaction as the change and delivered by a job, so they exist exactly when the change committed.
- **Options pattern** for every setting, validated when the API starts.
- **Injected clock.** Everything reads time from `TimeProvider`, so tests can move time.

## Database

PostgreSQL is the source of truth, and the rules that must never break are enforced by the database itself (design doc 5.4), so a bug in application code still cannot oversell stock.

| Table | Holds |
| --- | --- |
| `identity_users` (+ claims, logins, tokens) | Login data from Identity: email, password hash, lockout |
| `users` | Display name, role, reliability score, suspension (same id as the Identity user) |
| `refresh_tokens` | Hashed refresh tokens and their rotation chain |
| `organizations`, `organization_members` | Businesses and who belongs to them as Owner or Staff |
| `listings` | Food offered for pickup, with live stock |
| `reservations` | Claims on listings, with pickup codes |
| `notifications` | In-app messages (`data` is jsonb) |
| `audit_logs` | Every state change, append-only |
| `outbox_messages` | Messages waiting to be delivered |

**Constraints and indexes** include:

- stock between 0 and the total;
- price between 0 and the MRP;
- a pickup window that ends after it starts;
- a unique pickup code;
- a partial unique index allowing one active reservation per customer per listing;
- a covering partial index of live listings for search (Week 5, decision 11);
- an index for polling the outbox;
- row versions (`xmin`) on listings and reservations.

**The critical statement** (from `AppDbContext.TryTakeStockAsync`): the reservation only goes ahead if this UPDATE changes one row.

```sql
UPDATE listings
SET    quantity_available = quantity_available - @qty,
       status = CASE WHEN quantity_available - @qty = 0 THEN 'SoldOut' ELSE status END
WHERE  id = @listingId AND status = 'Active'
  AND  quantity_available >= @qty AND pickup_end > @now
```

## Security

| Concern | What the API does |
| --- | --- |
| Passwords | ASP.NET Core Identity (salted PBKDF2). Five wrong passwords lock the account for 15 minutes. |
| Sessions | 15-minute JWT access tokens in the Authorization header. Refresh tokens are 256 random bits, stored only as SHA-256 hashes, sent in an httpOnly, Secure, SameSite=Strict cookie limited to `/api/v1/auth`, rotated on every use. Reusing a token rotated more than 30 seconds ago ends every session of that account; inside that window it only gets an access token (decision 10). |
| Authorization | Role policy for admin endpoints. Business membership is checked in the database on every request, not stored in the token, so removing someone takes effect immediately. Other people's reservations and unverified businesses answer 404, not 403, so ids are not confirmed. |
| Brute force | Rate limits on register, login and refresh (per IP) and on pickup code checks (per user). Pickup codes are 8 characters from 31 (about 8.5 x 10^11 values). |
| Injection | EF Core parameterises every query; the few hand-written statements use interpolated parameters, never string concatenation. |
| Uploads | Type checked from the file's bytes, 10 MB limit, random file names, served with `nosniff`. |
| Transport and browser | HTTPS redirection and HSTS outside development; CORS allows only the frontend's origin; `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`. |
| Secrets | User secrets in development and environment variables elsewhere; nothing sensitive in the repository. The API will not start without them. |
| Logging | Serilog with a correlation id; tokens and passwords are never logged (the SignalR token in the query string included, since request paths are logged without it). Unexpected errors return no internal details. |

## Error handling and validation

Every error is RFC 9457 ProblemDetails (`application/problem+json`) with a stable `code` the frontend switches on, a readable `detail`, field errors for validation, and a `traceId`. The codes are listed in [docs/API.md](docs/API.md#errors).

- **Request validation:** FluentValidation runs in an endpoint filter before the handler. All field errors come back at once, keyed by the JSON field name.
- **Business rules:** the entities throw a `DomainException` with a code (for example `SOLD_OUT`) and, for input mistakes, the field it belongs to.
- **Access:** use cases throw `AppException` for 401, 403 and 404.
- **One exception handler** maps these, plus concurrency conflicts and malformed JSON, to ProblemDetails. Unknown routes and 401/403 from the authentication middleware use the same shape.
- **Logging:** each request writes one line, from the request log. Expected answers (validation, 404, sold out, a used code) are Information; only unexpected failures are Error, with the stack trace and the same trace id the client gets in its 500 response. EF Core's and the exception middleware's own copies of the same error are switched off, because they repeated the stack trace up to four times for one failure. `LoggingTests` checks this. For example, with the database password wrong, one request now logs:

  ```
  [08:29:11 ERR] HTTP GET /api/v1/listings responded 500 in 432.7543 ms 0HNPGLU1TK782:00000001
  Npgsql.PostgresException (0x80004005): 28P01: password authentication failed for user "plateback"
     at ...
  ```

  and the client receives `{"code":"INTERNAL_ERROR", ..., "traceId":"0HNPGLU1TK782:00000001"}`, so a user's report leads straight to the line.

## Testing

```bash
dotnet test
```

**181 tests, all passing** (line coverage 88%, methods 95.6%):

- **Domain (55):** the state rules of every entity. Examples: a collected reservation cannot be cancelled; the pickup window must be 15 to 720 minutes (14 and 721 fail); reliability stays between 0 and 100; a no-show is applied once.
- **Application (24):** validators, the distance formula, file-type detection and paging defaults.
- **Integration (102):** the real API hosted in memory with `WebApplicationFactory`, called over HTTP like the frontend would, against a real PostgreSQL database. Highlights:
  - Ten customers race for the last portion: exactly one gets it, nine get `SOLD_OUT`, stock ends at 0.
  - One customer double-clicking Reserve five times gets one reservation.
  - Two parallel cancels return the stock once.
  - Five wrong passwords lock the account; an expired token gives `TOKEN_EXPIRED`; replaying a rotated refresh token ends every session.
  - A pickup code from another business looks unknown; a code works only once.
  - A staff member removed from a business loses access on their next request, with the same token.
  - After the pickup window, the expiry job marks no-shows once, even when run twice.
  - A text file renamed to `.jpg` is rejected.
  - Two refreshes with the same cookie within 30 seconds keep the session (Week 4).
  - SignalR: a visitor hears about stock changes, only a business's own staff hear about its queue, a customer cannot join a business's group, and notifications are pushed once the outbox delivers them (Week 4).
  - A storage failure (a full disk) gives one Error log entry carrying the trace id the client sees; expected 4xx answers give none (Week 4).
  - Week 5, from the coverage report: the dashboard listings order and paging, owners (not staff) editing business details, admin listing removal notifying customers and staff, finding, suspending and restoring accounts (a suspended user's live token cannot reserve), marking notifications read, and a broken outbox message retried five times without blocking the others.

Every test is listed with its purpose in the frontend repository's [test catalogue](https://github.com/mhrjnrahul/PlateBack---Frontend/blob/main/docs/test-catalogue.md). GitHub Actions runs the same suite against PostgreSQL 16 on every push.

The integration tests use the database `plateback_test` on localhost with the `plateback` login (override with the `PLATEBACK_TEST_DB` environment variable). It is created on the first run. Before each test, all tables are emptied and the demo data loaded again. A test clock fixes "now" so the dashboard's "today" is always the same day.

## Development process

The work took about 30 to 35 hours, within the time set for the Week 3 task.

1. **Design first.** The Week 1 design document had already fixed the tables, constraints, endpoints, error codes and business rules. The Week 2 frontend's mock API had then made them concrete. The backend follows both, so Week 4 can switch the frontend over without changing its pages.
2. **Plan and decisions.** A step plan was written before coding, and each choice that differs from the design or needs a reason is logged in [docs/decisions.md](docs/decisions.md). Examples: plain handlers instead of MediatR, hosted services instead of Hangfire, how the Identity user relates to the domain user.
3. **Inside out.** The domain and its unit tests came first, then the use cases, the database mapping and migration, the API, and finally the integration tests against PostgreSQL.
4. **Checked as a client would.** Besides the tests, the running API was called with curl; the examples in [docs/API.md](docs/API.md) are those real responses. Every step ended with a clean build (warnings fail the build), `dotnet format` and all tests passing.

## Known limitations

- **Recurring listings (F09)** and their daily job are not built. The frontend has no screen for them yet.
- **Photos are stored on the API's disk.** Fine for one server; cloud storage is planned for deployment (Week 6).
- **SignalR on one server.** Pushes go to clients connected to this instance. Running several instances would need a Redis backplane. Pushes are best effort, so pages still poll once a minute.
- **No Docker setup.** PostgreSQL is installed locally. Containers can be added for deployment if Week 6 needs them.
- **Email notifications (F20)** are not sent. The outbox job is where they would be added.
