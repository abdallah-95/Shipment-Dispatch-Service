# Shipment Dispatch Service

## Context

Backend for a courier dispatch console: .NET 8 Web API, SQL Server, EF Core.
No front end. Built to be maintained for years, not demoed once.

Anyone must be able to clone the repo onto a clean machine and run
`docker compose up` with nothing else installed or configured. If that breaks,
nothing else matters.

Scope is deliberately limited to the core requirements. Prefer a smaller thing
done correctly over more features. Do not add scope without asking.

## How we work

- Work proceeds in phases. Do only the phase you are asked for, then stop and
  report: what changed, how it was verified, and anything uncertain.
- For non-trivial work, propose a short plan before writing code.
- I write all commits. Never run `git commit`, `git push`, or rewrite history.
- Never claim a performance characteristic without a measured number from the
  full seeded dataset. Show the query plan when discussing indexes.
- If something in this file conflicts with what you would otherwise do, follow
  this file and say so. If you think a rule here is wrong, say why — do not
  silently deviate.
- Do not write DESIGN.md or AI_NOTES.md. I write those. You may draft README
  run steps when asked.

## Solution structure

Layered architecture. No MediatR, no CQRS ceremony, no generic repository over
DbContext — the scope does not justify the indirection.

```
src/
  Dispatch.Domain/          entities, enums, transition rules — no dependencies
  Dispatch.Application/     services, DTOs, validation — references Domain
  Dispatch.Infrastructure/  DbContext, configurations, migrations, interceptors
  Dispatch.Api/             controllers, middleware, DI composition
  Dispatch.Seeder/          console app — references Infrastructure, NOT Api
tests/
  Dispatch.Tests/           xUnit, run in a container against the compose db
docker/
  sqlserver/Dockerfile      official SQL Server image + mssql-server-fts
Dockerfile                  one file, two targets: api and seeder
docker-compose.yml
```

Dependencies point inward. Domain references nothing.

## API style

The HTTP layer uses controllers, not Minimal APIs.

- One controller per resource in `Dispatch.Api/Controllers`, inheriting
  `ControllerBase` with `[ApiController]`.
- Controllers stay thin: bind input, call an Application-layer service, map the
  result to an HTTP response. No business rules and no EF queries in controllers.
- Every action takes a `CancellationToken` and returns `ActionResult<T>`.
- Model-validation failures and all other errors return ProblemDetails.
- Do not mix in Minimal APIs.

## Domain

Entities: Customer (~5,000), Driver (~200), Shipment (50,000+), ShipmentStatusHistory.

Shipment fields: Id, TrackingNumber, CustomerId, DriverId (nullable), Origin,
Destination, WeightKg, Status, CreatedAt, UpdatedAt, Version, SearchText.

### Status transitions — encode as data, not as a switch

```
Created        -> PickedUp
PickedUp       -> InTransit
InTransit      -> OutForDelivery
OutForDelivery -> Delivered | Failed
Delivered      -> Returned
Failed         -> (terminal)
Returned       -> (terminal)
```

- No skipping states. No re-entering a state.
- Failed is terminal: a failed shipment's goods re-enter as a new shipment.
- Returned means a post-delivery return only.
- Hold the table in a single static readonly map. It is the source for both
  validation and the `allowedTransitions` field in 409 responses.

## Time

- UTC everywhere. `datetime2` columns.
- Inject `TimeProvider`. Never call `DateTime.UtcNow` or `DateTime.Now` directly.

## Concurrency

- Managed `int Version` on Shipment, via an `IVersioned` interface, configured
  with `IsConcurrencyToken()`. NOT SQL Server `rowversion`.
- The version is incremented by `VersionInterceptor`, never by hand in handlers.
- The client sends `expectedVersion` in the POST body. If absent, return 400
  ProblemDetails.
- Check order in the status handler: load the shipment, then compare
  `expectedVersion` to the loaded version and return 409 immediately on
  mismatch, THEN validate the transition. A stale client must get a 409
  describing the current state, not a 400 for a transition it never requested.
  The save-time token check still catches the race between load and save.
- Before saving, set the original value to the client's version so EF checks
  against what the client saw, not what was just read:
  `db.Entry(shipment).Property(s => s.Version).OriginalValue = request.ExpectedVersion;`
- The status change and its history row are written in ONE `SaveChangesAsync`
  call. Never two.
- On `DbUpdateConcurrencyException`: return 409 ProblemDetails with
  `currentStatus`, `currentVersion`, and `allowedTransitions` from the current
  status. Do not retry. Discard the context after the exception.

## Interceptors

- `AppDbContext` overrides nothing. All save-time cross-cutting behaviour lives
  in `SaveChangesInterceptor` implementations.
- Registration order: `SearchTextInterceptor` FIRST, `VersionInterceptor` SECOND.
  Comment the registration explaining why.
- Both interceptors implement BOTH `SavingChanges` and `SavingChangesAsync`.
  EF Core does not route one to the other.
- `SearchTextInterceptor` resolves customer names in ONE batched query, never
  one query per shipment.

## Banned APIs

- `ExecuteUpdate` / `ExecuteUpdateAsync` / `ExecuteDelete` on Shipment are
  prohibited, because they bypass the version increment. The ONLY exception is
  setting `SearchText`, which must not bump the version.
- No raw SQL for writes to Shipment.

## Search

- `SearchText` is a denormalised column on Shipment: tracking number, customer
  name and destination, normalised (lowercase, trimmed, whitespace collapsed).
  The same normalisation function is used when writing and when querying.
- One SQL Server full-text index on `SearchText`, `STOPLIST = OFF`.
- The full-text catalog and index are created with raw SQL inside a migration,
  with a matching `Down`.
- Matching is word-prefix, not substring. Do not implement or claim
  `LIKE '%term%'` semantics.

## List endpoint contract

`GET /api/shipments`

- Page size default 25, hard cap 100. Clamp oversized values; do not error.
- Filters: status, created-date range, driver. Combined with AND.
- One `search` parameter across tracking number, customer name, destination.
- Every sort has a deterministic tie-breaker on `Id`.
- The count query and the page query apply IDENTICAL predicates. Build the
  filtered query once and derive both from it.
- Reject unknown sort fields with 400. Do not silently ignore them.
- Rows include core fields plus customer name and driver name.
- Response includes total count. Map to DTOs; never return entities.

## Errors

- All error responses are RFC 7807 ProblemDetails, including 409.

## Schema and seeding

- Schema comes from EF migrations. Never `EnsureCreated`.
- The API does NOT migrate and does NOT seed on startup.
- The seeder applies migrations, then seeds, then exits.
- Insert with `SqlBulkCopy`, not through the change tracker.
- Use a fixed random seed so the dataset is identical on every machine.
- Idempotent: if shipments exist, log "already seeded, skipping" and exit 0.
- No reset command.
- Seed data must be internally consistent:
  - status history is the complete chain to the current status, ascending timestamps
  - `UpdatedAt` equals the last history timestamp
  - `Version` reflects the number of updates
  - `SearchText` populated
- After inserting, poll `FULLTEXTCATALOGPROPERTY(..., 'PopulateStatus')` until
  population completes, then exit 0.
- Exit codes: 0 on success or skip, non-zero on failure.

## Docker

- Single Dockerfile with two final targets (`api`, `seeder`) sharing restore
  and build layers. Multi-stage; the host needs no .NET SDK.
- A third target, `tests`, builds on the SDK image and runs `dotnet test`. It is
  only started through the compose `tests` profile, so a plain `docker compose up`
  never runs it.
- SQL Server image built locally from the official image plus `mssql-server-fts`.
- Pin image tags. Never `latest`.
- Healthcheck uses `/opt/mssql-tools18/bin/sqlcmd` with `-C`.
- Compose order: `db` healthy -> `seed` completes successfully -> `api` starts.
  Use `condition: service_healthy` and `condition: service_completed_successfully`.
- Must not require a `.env` file. The local SA password is inline in compose,
  commented as a local-only throwaway.
- `.dockerignore` excludes `bin/`, `obj/`, `.vs/`, `.git/`.
- Development is on Windows: any shell script must have LF line endings.
  Add `.gitattributes` with `*.sh text eol=lf`.

## Observability

- Serilog, JSON to console. Message templates with named properties — never
  string interpolation in log calls.
- Correlation id middleware: take W3C `traceparent` if present, else generate.
  Push it into the log context. Return it on the response header.
- `/health/live` (process up) and `/health/ready` (includes a SQL Server check).
- Never log personal data beyond what is needed to identify a record.

## Tests

- xUnit. Tests run as a compose service inside a Linux container against the
  compose `db` service (the same custom SQL Server image, so tests get full-text
  search). Never the EF in-memory provider or SQLite: neither enforces
  concurrency tokens or supports full-text search.
- Each run creates a uniquely named database, applies migrations, and drops it
  at the end. One shared fixture per test run.
- Tests create their own data and never read the seeded dataset.
- Required:
  - every legal transition succeeds
  - every illegal pair is rejected — loop over the transition table, do not
    hand-pick cases
  - deterministic concurrency test: two contexts, same version, first save
    wins, second throws, exactly one new history row
  - parallel HTTP concurrency test: one 2xx, one 409, exactly one new history row
  - stale `expectedVersion` returns 409
  - missing `expectedVersion` returns 400
  - recomputing `SearchText` alone does not change `Version`
- After writing a concurrency test, demonstrate it fails against a deliberately
  broken implementation, then restore. A test that passes against broken code
  is worthless.

## Code conventions

- Nullable reference types enabled.
- `async` all the way. Never `.Result` or `.Wait()`.
- Pass `CancellationToken` through every async call.
- No secrets in committed files.
