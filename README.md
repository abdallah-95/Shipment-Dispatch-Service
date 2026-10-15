# Shipment Dispatch Service

Backend for a courier dispatch console: .NET 8 Web API, SQL Server, EF Core.

The only prerequisite is Docker with Compose v2. No .NET SDK, SQL Server, or configuration is needed.

Platform note: the SQL Server image is amd64-only. On Apple Silicon, enable Rosetta in Docker Desktop.

## Everything in one command

Build and start the stack, wait until the database is seeded and the API is up, then run the tests:

```
docker compose up --build -d --wait && docker compose --profile tests run --rm --build tests
```

The stack keeps running afterwards. The command's exit code is the test result, and the tests are skipped if the stack fails to come up. The two halves are described separately below.

Needs `&&`, so use bash, zsh, cmd, or PowerShell 7+. In Windows PowerShell 5.1, run the two commands on separate lines.

### Run everything, then stop the stack

Same as above, but the stack is stopped and removed at the end, whether or not the tests passed. The database volume is kept, so the next start skips seeding.

bash / zsh (the parentheses keep `exit` from closing your shell; the exit code is the test result):

```
(docker compose up --build -d --wait && docker compose --profile tests run --rm --build tests; status=$?; docker compose down; exit $status)
```

Windows PowerShell:

```
docker compose up --build -d --wait; if ($LASTEXITCODE -eq 0) { docker compose --profile tests run --rm --build tests }; docker compose down
```

Add `-v` to `docker compose down` to also delete the database volume.

## Start the service

```
docker compose up --build
```

This starts three services in order:

1. `db` – SQL Server 2022 with full-text search. Compose waits until its healthcheck passes.
2. `seed` – applies the EF Core migrations, loads the sample data, then exits. Compose waits for it to exit successfully.
3. `api` – the Web API, on http://localhost:8080.

The first build takes a couple of minutes (it downloads the base images and the full-text component). Later starts reuse the cache.

Check it is up:

```
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready     # also checks the SQL Server connection
curl http://localhost:8080/api/shipments/1  # a seeded shipment, with customer, driver and status history
```

An unknown shipment id returns a 404 `application/problem+json` response. All error responses are ProblemDetails.

## Swagger

http://localhost:8080/swagger (and `/` redirects there). It documents both endpoints, their responses and an example request body.

Swagger is enabled by the `Swagger:Enabled` setting, which defaults to false. `docker-compose.yml` sets it to true for the local stack (`Swagger__Enabled`). With the setting unset, `/swagger` returns 404.

## API at a glance

Two endpoints so far. The examples use shipment 47826, which on a freshly seeded database is `Created` at version 0, and driver 1. They change that shipment, so run them in order, and `docker compose down -v` then `docker compose up --build` to start over. `traceId` and `updatedAt` differ on every run. Shell syntax is bash/zsh.

**Get a shipment** (customer, driver, version and full status history):

```
curl -s -w '\nHTTP %{http_code}\n' http://localhost:8080/api/shipments/47826
```

```
{"id":47826,"trackingNumber":"DSP10047826","status":"Created","version":0,"origin":"Westfield Depot","destination":"Saint Albans","weightKg":3.07,"createdAt":"2026-09-30T00:36:29Z","updatedAt":"2026-09-30T00:36:29Z","customer":{"id":1501,"name":"Bridgeport Bakery Inc"},"driver":null,"history":[{"id":236761,"fromStatus":null,"toStatus":"Created","changedAt":"2026-09-30T00:36:29Z"}]}
HTTP 200
```

**Change status.** `expectedVersion` is the `version` from your last GET or POST response. `driverId` is required when moving to `PickedUp`, and only then.

```
curl -s -w '\nHTTP %{http_code}\n' -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -d '{"status":"PickedUp","expectedVersion":0,"driverId":1}'
```

```
{"id":47826,"status":"PickedUp","driverId":1,"updatedAt":"2026-10-15T14:05:49.6547241Z","version":1}
HTTP 200
```

The response carries the new `version`; send it as `expectedVersion` on the next change. Sending the same request again is now stale:

```
curl -s -w '\nHTTP %{http_code}\n' -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -d '{"status":"PickedUp","expectedVersion":0,"driverId":1}'
```

```
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Version conflict","status":409,"detail":"Shipment 47826 is at version 1, not 0. Re-read it and retry.","traceId":"00-b705f35b85e691f3c38181fd86e8b691-3a8ff524cf5504ae-00","currentStatus":"PickedUp","currentVersion":1,"allowedTransitions":["InTransit"]}
HTTP 409
```

An illegal transition (here, `PickedUp` to `Delivered`):

```
curl -s -w '\nHTTP %{http_code}\n' -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -d '{"status":"Delivered","expectedVersion":1}'
```

```
{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Illegal status transition","status":422,"detail":"Shipment 47826 cannot move from PickedUp to Delivered.","traceId":"00-7d29b560f162ae1493f20267052a858b-b6c7736cb49021f6-00","currentStatus":"PickedUp","allowedTransitions":["InTransit"]}
HTTP 422
```

What the error codes mean (all are `application/problem+json`):

| Code | Meaning |
| --- | --- |
| 400 | The request is malformed: `status` or `expectedVersion` is missing or invalid, or `driverId` was sent with a status other than `PickedUp`. |
| 404 | No shipment with that id. |
| 409 | `expectedVersion` is not the shipment's current version, or a concurrent request got there first. The body has `currentStatus`, `currentVersion` and `allowedTransitions`. Re-read and retry. |
| 422 | The request is well-formed but not allowed: an illegal transition, or `PickedUp` without the id of an existing driver. For an illegal transition the body has `currentStatus` and `allowedTransitions`. |

## Status workflow

| From | Can move to |
| --- | --- |
| Created | PickedUp |
| PickedUp | InTransit |
| InTransit | OutForDelivery |
| OutForDelivery | Delivered, Failed |
| Delivered | Returned |
| Failed | (terminal) |
| Returned | (terminal) |

No skipping states and no re-entering a state.

- Failed is terminal: a failed shipment's goods re-enter as a new shipment.
- Returned means a post-delivery return only.

## Assumptions

- Moving to `PickedUp` requires a `driverId` that names an existing driver, otherwise 422. A `driverId` sent with any other transition is 400.
- An illegal transition, including a request for the status the shipment is already in, returns 422.
- A stale `expectedVersion` returns 409, and so does losing a race with a concurrent request. If the winner already moved the shipment to the requested status, the loser still gets 409, not success. There is no idempotent-success case.
- `expectedVersion` is checked before the transition, so a stale client gets the current state in a 409 rather than a 422 about a transition it never asked for.
- All timestamps are UTC.

## Seeding

Seeding is its own step: the API never migrates or seeds. The `seed` service creates 5,000 customers, 200 drivers and 50,000 shipments with their status history, using a fixed random seed, so the data is identical on every machine.

It is idempotent. If shipments already exist it logs `already seeded, skipping` and exits 0, so restarting the stack is safe. There is no reset command. To start over, delete the database volume:

```
docker compose down -v
docker compose up --build
```

To see the seeder's output: `docker compose logs seed`.

## Database

SQL Server is not published to the host: it is reachable only from the other containers, as `db:1433`. To run queries, use `sqlcmd` inside the container:

```
docker compose exec db bash -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d Dispatch -Q "SELECT COUNT(*) FROM Shipments"'
```

The SA password in `docker-compose.yml` is a local-only throwaway; see the comment there.

## Run the tests

```
docker compose --profile tests run --rm tests
```

The tests run inside a Linux container against the same `db` service (only `db` is started; `seed` and `api` are not needed). Each run creates its own `dispatch_test_<guid>` database, applies the migrations, and drops it at the end. Tests create their own data and never read the seeded dataset. The command's exit code is the test result, so it works in scripts and CI.

A plain `docker compose up` never runs the tests.

## Stop

```
docker compose down       # stop and remove containers, keep the data
docker compose down -v    # also delete the database volume
```
