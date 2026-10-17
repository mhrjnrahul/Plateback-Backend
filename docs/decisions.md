# Decision log

One entry per decision: the problem, the options considered, the choice and what it costs. Newest at the bottom.

## Template

### N. Title (date)

- **Problem:**
- **Options:**
- **Choice:**
- **Trade-off:**

## Decisions

### 1. Plain handler classes instead of MediatR (14 Oct 2026)

- **Problem:** The design document uses MediatR to send each request to its handler. From version 13 MediatR needs a commercial licence, and the licence has caused trouble before.
- **Options:** Stay on MediatR 12.x (the last free version, no further updates). Use the MIT-licensed Mediator library, which works much the same way. Write plain handler classes and register them with dependency injection.
- **Choice:** Plain handler classes. Each use case is one class in `PlateBack.Application` (for example `ReserveListingHandler`) with a `HandleAsync` method, and the endpoint receives the handler it needs.
- **Trade-off:** There is no shared pipeline that runs for every request, so validation and logging are called explicitly or added with an endpoint filter. In return there is one dependency fewer, no licence risk, and a reader can go straight from an endpoint to its handler.

### 2. Shouldly for test assertions (14 Oct 2026)

- **Problem:** Tests need readable checks and clear failure messages. FluentAssertions, the common choice, is commercially licensed from version 8.
- **Options:** xUnit's built-in `Assert`, FluentAssertions 8, Shouldly.
- **Choice:** Shouldly (MIT licence).
- **Trade-off:** One extra test package. Failures read as "problem.Code should be SOLD_OUT but was LIMIT_REACHED", which saves opening a debugger.

### 3. Separate domain User linked to the Identity user (14 Oct 2026)

- **Problem:** ASP.NET Core Identity (chosen in design doc 4.6) stores accounts in `IdentityUser`, which lives in a package the Domain project must not reference. The domain still needs a user for business rules: reliability score, role, suspension and the reservation limit below 60 reliability.
- **Options:** (a) A domain `User` in its own table with the same `Guid` id as the Identity user. (b) Put reliability and role on `ApplicationUser : IdentityUser<Guid>` in Infrastructure and keep the rules in a separate domain class. (c) Let the Domain reference Identity, which breaks the dependency rule. (d) Drop most of Identity, keep only its password hasher and write lockout ourselves.
- **Choice:** (a). `ApplicationUser : IdentityUser<Guid>` in Infrastructure holds login data (email, password hash, lockout). The domain `User` holds display name, role, reliability score and suspension, with methods such as `ApplyCollected()` and `ApplyNoShow()`. Both rows are created in one `SaveChanges` during registration, since they share one `DbContext`.
- **Trade-off:** Two tables instead of one, and the email lives only in the Identity table, so it is read from there rather than copied. In return the domain is tested without Identity, and password hashing and lockout stay with Microsoft's tested code.

### 4. Errors: domain exceptions with codes, mapped to ProblemDetails in one place (17 Oct 2026)

- **Problem:** Business rules live in the domain, but the client needs an HTTP status and a stable `code` (design doc 6.4) for each failure.
- **Options:** Return `Result` objects from every method. Throw exceptions that carry the code. Map errors in each endpoint.
- **Choice:** The domain throws `DomainException(code, message, field)`; use cases throw `AppException(status, code, ...)` for 401/403/404. One `IExceptionHandler` turns both into ProblemDetails. Request shape errors come from FluentValidation in an endpoint filter, before the handler runs.
- **Trade-off:** Exceptions for expected outcomes are slower than results and invisible in method signatures. At this request volume the cost does not matter, and every rule reads as one line (`throw new DomainException(ErrorCodes.SoldOut, ...)`). Expected exceptions are excluded from error logging so the logs show only real faults.

### 5. Background jobs as hosted services instead of Hangfire (17 Oct 2026)

- **Problem:** Design doc 4.5 runs the outbox every 10 seconds and expiry every minute with Hangfire.
- **Options:** Hangfire with PostgreSQL storage. A `BackgroundService` with `PeriodicTimer` in the API process.
- **Choice:** `BackgroundService`. Hangfire's recurring jobs use cron with one-minute resolution, so the 10-second outbox would need a workaround, and it adds a dashboard and its own tables that nothing uses yet.
- **Trade-off:** No retry dashboard or job history. Both jobs claim rows with `FOR UPDATE SKIP LOCKED` and only change rows that are still due, so they are safe to run twice or on two servers. Hangfire can still be added if Week 6 needs scheduling across instances.

### 6. Stock changes with one conditional UPDATE, plus row versions (17 Oct 2026)

- **Problem:** Two customers reserving the last unit at the same moment must not both succeed (goal G1). Reading the stock, checking it and writing it back leaves a gap between read and write.
- **Options:** Serializable transactions with retries. Lock the listing row (`SELECT ... FOR UPDATE`) for the whole request. One conditional `UPDATE ... WHERE quantity_available >= @qty` (design doc 5.5).
- **Choice:** The conditional UPDATE for stock, written as parameterised SQL through EF Core. The customer's own row is locked (`FOR UPDATE`) so their parallel requests cannot both pass the limit checks. Listings and reservations carry PostgreSQL's `xmin` as a row version, so an edit or a cancel based on a stale copy fails with 409 instead of overwriting.
- **Trade-off:** A little hand-written SQL beside EF Core. Proven by tests: ten parallel customers for one unit give exactly one success, and two parallel cancels return the stock once.

### 7. Integration tests on real PostgreSQL, reset by the seeder (17 Oct 2026)

- **Problem:** The rules that matter most (CHECK constraints, unique indexes, row locks, transactions) are enforced by PostgreSQL. The EF Core in-memory provider ignores all of them. There is no Docker on the development machine for Testcontainers.
- **Options:** In-memory provider. SQLite. Testcontainers. A local `plateback_test` database.
- **Choice:** A local test database, created by migrations on the first run. Before each test the demo seeder truncates every table and inserts the demo data again, so Respawn was not needed. A test clock (`TimeProvider`) lets tests move time forward for token expiry and no-shows.
- **Trade-off:** Tests need PostgreSQL running and run one at a time (about 45 seconds for 80 tests). In return they test the same database the API uses in production.

### 8. Local disk for uploaded photos (17 Oct 2026)

- **Problem:** Listings can have photos (added in Week 2), so the API needs somewhere to store them.
- **Options:** Store bytes in PostgreSQL. A cloud bucket (S3, Azure Blob). A folder served by the API.
- **Choice:** A folder (`uploads/`) behind an `IImageStorage` interface, served as static files with `X-Content-Type-Options: nosniff`. The file type is checked from its first bytes and the name is random.
- **Trade-off:** Works for one server only. Week 6 can swap in cloud storage without touching the use cases.

### 9. SignalR pushes ids, the browser refetches (Week 4)

- **Problem:** The frontend polled stock every 15 seconds, so a customer could look at a portion that was already gone, and the counter queue lagged behind new reservations. The Week 4 brief asks for real-time interaction.
- **Options:** Faster polling. Push the changed data itself over SignalR. Push only "this changed" with ids, and let the browser refetch through the normal endpoints.
- **Choice:** Ids only, through one hub (`/hubs/live`). Handlers call an `ILiveUpdates` port after their transaction commits; the Api project implements it with SignalR. Stock goes to everyone, business queues only to members who joined their group (checked in the database), notifications only to their users.
- **Trade-off:** One extra request per change for each open page, instead of none. In return there is one source of truth: no second serializer to keep in step with the DTOs, no permission rules to repeat in the hub, and nothing can arrive out of order and show stale stock. Pushes are best effort, so the browser keeps polling slowly (every minute) as a safety net. One server only; several would need a Redis backplane.

### 10. A 30-second grace window for rotated refresh tokens (Week 4)

- **Problem:** Connecting the frontend showed that two refreshes often arrive with the same cookie: React runs effects twice in development, two tabs restore their session together, and a page reloading mid-refresh never receives the new cookie. The reuse detection from Week 3 took the second one as theft and ended the session.
- **Options:** Drop reuse detection. Let the frontend send one refresh at a time and change nothing on the server. Accept a just-rotated token for a short time.
- **Choice:** Both of the last two. The frontend shares one refresh request. The API accepts a token rotated less than 30 seconds ago and answers with an access token only, leaving the browser's newer cookie alone. Reuse after that still revokes every session.
- **Trade-off:** A stolen token replayed within 30 seconds of its rotation gets one 15-minute access token, not a session. Two tabs, double mounts and reloads no longer sign people out.
