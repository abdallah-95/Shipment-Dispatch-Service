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
2. `seed` – applies the EF Core migrations, loads the sample data, waits until full-text search covers every shipment, then exits. Compose waits for it to exit successfully.
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

http://localhost:8080/swagger (and `/` redirects there). It documents every endpoint, its parameters and responses, and an example request body.

Swagger is enabled by the `Swagger:Enabled` setting, which defaults to false. `docker-compose.yml` sets it to true for the local stack (`Swagger__Enabled`). With the setting unset, `/swagger` returns 404.

## API at a glance

Getting and changing one shipment. Listing and searching is [further down](#list-and-search-shipments). The examples use shipment 47826, which on a freshly seeded database is `Created` at version 0, and driver 1. They change that shipment, so run them in order, and `docker compose down -v` then `docker compose up --build` to start over. `traceId`, `correlationId` and `updatedAt` differ on every run. Shell syntax is bash/zsh.

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
{"id":47826,"status":"PickedUp","driverId":1,"updatedAt":"2026-10-16T04:41:21.5826874Z","version":1}
HTTP 200
```

The response carries the new `version`; send it as `expectedVersion` on the next change. Sending the same request again is now stale:

```
curl -s -w '\nHTTP %{http_code}\n' -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -d '{"status":"PickedUp","expectedVersion":0,"driverId":1}'
```

```
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Version conflict","status":409,"detail":"Shipment 47826 is at version 1, not 0. Re-read it and retry.","traceId":"00-f69526efc91f2511b08a51746442abbd-0a15624436853dae-00","correlationId":"f69526efc91f2511b08a51746442abbd","currentStatus":"PickedUp","currentVersion":1,"allowedTransitions":["InTransit"]}
HTTP 409
```

An illegal transition (here, `PickedUp` to `Delivered`):

```
curl -s -w '\nHTTP %{http_code}\n' -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -d '{"status":"Delivered","expectedVersion":1}'
```

```
{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Illegal status transition","status":422,"detail":"Shipment 47826 cannot move from PickedUp to Delivered.","traceId":"00-0a90c672a1ba22746c2b3bf0625bdd74-27a7fff2382ecd04-00","correlationId":"0a90c672a1ba22746c2b3bf0625bdd74","currentStatus":"PickedUp","allowedTransitions":["InTransit"]}
HTTP 422
```

What the error codes mean (all are `application/problem+json`):

| Code | Meaning |
| --- | --- |
| 400 | The request is malformed: `status` or `expectedVersion` is missing or invalid, or `driverId` was sent with a status other than `PickedUp`. |
| 404 | No shipment with that id. |
| 409 | `expectedVersion` is not the shipment's current version, or a concurrent request got there first. The body has `currentStatus`, `currentVersion` and `allowedTransitions`. Re-read and retry. |
| 422 | The request is well-formed but not allowed: an illegal transition, or `PickedUp` without the id of an existing driver. For an illegal transition the body has `currentStatus` and `allowedTransitions`. |
| 500 | An unexpected error. The body is deliberately generic: no exception message or stack trace. |

Every error body has a `correlationId`, the same value as the `X-Correlation-Id` response header. Quote it when reporting a problem; it finds every log line for the request (see [Finding a request in the logs](#finding-a-request-in-the-logs)).

## List and search shipments

`GET /api/shipments` returns one page of shipments, newest first unless you choose another sort.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `page` | 1 | 1-based page number. |
| `pageSize` | 25 | Rows per page. Values above 100 are clamped to 100; the response says which size was used. |
| `status` | | Only this status: `Created`, `PickedUp`, `InTransit`, `OutForDelivery`, `Delivered`, `Failed` or `Returned` (case-insensitive). |
| `createdFrom` | | Only shipments created at or after this instant. |
| `createdTo` | | Only shipments created before this instant (exclusive). |
| `driverId` | | Only shipments assigned to this driver. |
| `search` | | Words to find in the tracking number, customer name and destination. See [Search](#search). |
| `sortBy` | `createdAt` | `createdAt`, `updatedAt`, `status`, `trackingNumber`, `customerName` or `destination`. `status` sorts by name, alphabetically. |
| `sortDir` | `desc` | `asc` or `desc`. |

Filters combine with AND. Every sort ends with the shipment id in the same direction, so rows with equal sort values always come back in the same order and paging never skips or repeats a row.

Dates are ISO-8601 instants with `Z` or an offset, such as `2026-01-01T00:00:00Z`. A value without an offset is rejected, not guessed. In a query string `+` means a space, so send `+02:00` as `%2B02:00`.

The response is `{"items":[...],"page":1,"pageSize":25,"totalCount":50000}`. `totalCount` counts every shipment matching the filters, not just this page. Each item has `id`, `trackingNumber`, `customerId`, `customerName`, `driverId`, `driverName` (both null until the shipment is picked up), `origin`, `destination`, `weightKg`, `status`, `createdAt`, `updatedAt` and `version`.

A 400 `application/problem+json` response, with `errors` keyed by parameter name, means:

- `page` or `pageSize` is below 1 or not a number, or `page` is so large that the row offset overflows
- `status`, `sortBy` or `sortDir` is not one of the values above (a number such as `status=1` is rejected too)
- `createdFrom` or `createdTo` is not an instant with an offset, or `createdFrom` is after `createdTo`
- `search` is longer than 100 characters

### Search

- **Word prefix, not substring.** Each word you type must match the start of a word in the tracking number, customer name or destination. `port` finds "Port Said" and "Portsmouth"; `ort` finds nothing.
- **All words must match**, in any order: `cedar port` finds shipments with a word starting `cedar` and a word starting `port`.
- Case does not matter. Anything that is not a letter or digit separates words, so quotes, hyphens and `*` have no special meaning, and `AND`, `OR` and `NOT` are searched for as ordinary words.
- At most 100 characters, and only the first 8 words are used. A search with no letters or digits (such as `--`) returns an empty page.

### Examples

Shell syntax is bash/zsh. These read only, and are not affected by the status-change examples above.

A filtered list: shipments out for delivery, by customer name.

```
curl -s -w '\nHTTP %{http_code}\n' 'http://localhost:8080/api/shipments?status=OutForDelivery&sortBy=customerName&sortDir=asc&pageSize=2'
```

```
{"items":[{"id":45912,"trackingNumber":"DSP10045912","customerId":3323,"customerName":"Alpine Cosmetics Corp","driverId":158,"driverName":"Gabriel Quinn","origin":"Junction Hub","destination":"South Bend","weightKg":0.17,"status":"OutForDelivery","createdAt":"2026-09-27T10:53:44Z","updatedAt":"2026-09-30T17:42:39Z","version":3},{"id":45691,"trackingNumber":"DSP10045691","customerId":4791,"customerName":"Alpine Hardware & Sons","driverId":175,"driverName":"Chloe Garcia","origin":"Riverside Hub","destination":"Fort Worth","weightKg":34.60,"status":"OutForDelivery","createdAt":"2026-09-27T02:16:00Z","updatedAt":"2026-09-30T21:12:17Z","version":3}],"page":1,"pageSize":2,"totalCount":151}
HTTP 200
```

A search:

```
curl -s -w '\nHTTP %{http_code}\n' 'http://localhost:8080/api/shipments?search=cedar%20port&pageSize=2'
```

```
{"items":[{"id":49908,"trackingNumber":"DSP10049908","customerId":3010,"customerName":"Cedar Hardware GmbH","driverId":null,"driverName":null,"origin":"Midtown Depot","destination":"Port Louis","weightKg":19.30,"status":"Created","createdAt":"2026-09-30T23:57:38Z","updatedAt":"2026-09-30T23:57:38Z","version":0},{"id":49870,"trackingNumber":"DSP10049870","customerId":682,"customerName":"Cedar Furniture LLC","driverId":null,"driverName":null,"origin":"Midtown Depot","destination":"Port Elizabeth","weightKg":16.78,"status":"Created","createdAt":"2026-09-30T23:56:07Z","updatedAt":"2026-09-30T23:56:07Z","version":0}],"page":1,"pageSize":2,"totalCount":241}
HTTP 200
```

A search combined with filters:

```
curl -s -w '\nHTTP %{http_code}\n' 'http://localhost:8080/api/shipments?search=cedar%20port&status=Delivered&createdFrom=2026-01-01T00:00:00Z&createdTo=2026-07-01T00:00:00Z&pageSize=2'
```

```
{"items":[{"id":29260,"trackingNumber":"DSP10029260","customerId":1394,"customerName":"Cedar Glassworks Holdings","driverId":33,"driverName":"Carlos Nguyen","origin":"Eastside Depot","destination":"Portsmouth","weightKg":30.12,"status":"Delivered","createdAt":"2026-06-27T20:01:08Z","updatedAt":"2026-07-01T07:15:53Z","version":4},{"id":28933,"trackingNumber":"DSP10028933","customerId":2189,"customerName":"Cedarview Tools Partners","driverId":165,"driverName":"Amara Quinn","origin":"Lakeside Depot","destination":"Port Elizabeth","weightKg":17.00,"status":"Delivered","createdAt":"2026-06-24T21:13:36Z","updatedAt":"2026-06-27T11:33:57Z","version":4}],"page":1,"pageSize":2,"totalCount":66}
HTTP 200
```

Part of a word is not a match:

```
curl -s -w '\nHTTP %{http_code}\n' 'http://localhost:8080/api/shipments?search=ort&pageSize=1'
```

```
{"items":[],"page":1,"pageSize":1,"totalCount":0}
HTTP 200
```

An invalid parameter (`traceId` and `correlationId` differ on every run):

```
curl -s -w '\nHTTP %{http_code}\n' 'http://localhost:8080/api/shipments?sortBy=weight'
```

```
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"sortBy":["sortBy must be one of: createdAt, updatedAt, status, trackingNumber, customerName, destination."]},"traceId":"00-9fa73376bfb2477972cf194905395bcb-d2e388786f5c79fc-00","correlationId":"9fa73376bfb2477972cf194905395bcb"}
HTTP 400
```

Measured on the full seeded dataset (50,000 shipments) on the development machine, every list and search request took under 32 ms server-side (count and page together, median of 10 warm runs). The default sort and the created-date range are served by the `(CreatedAt, Id)` index; full-text search uses the `ShipmentSearch` catalog. The measurement write-up is kept locally and is not part of the repository.

## Dashboard

`GET /api/dashboard/summary` counts shipments by status:

```
curl -s -w '\nHTTP %{http_code}\n' http://localhost:8080/api/dashboard/summary
```

```
{"total":50000,"byStatus":[{"status":"Created","count":1532},{"status":"PickedUp","count":1594},{"status":"InTransit","count":729},{"status":"OutForDelivery","count":151},{"status":"Delivered","count":41157},{"status":"Failed","count":2708},{"status":"Returned","count":2129}]}
HTTP 200
```

This is after the status-change examples above, which move shipment 47826 from `Created` to `PickedUp`. On a freshly seeded database `Created` is 1533 and `PickedUp` 1593.

- `byStatus` always has all seven statuses, in workflow order, with 0 for a status no shipment is in.
- `total` is the sum of `byStatus`.
- The counts come from one `GROUP BY` query in the database, run on every request. Nothing is cached.

Measured on the full seeded dataset (50,000 shipments) on the development machine: a median of 12.3 ms server-side over 10 warm runs, of which the query took 11 ms.

## Health checks

| Endpoint | Checks | Answer |
| --- | --- | --- |
| `/health/live` | Nothing beyond the process answering. | 200 `Healthy` while the API is running. |
| `/health/ready` | Opens a connection to SQL Server. | 200 `Healthy`, or 503 `Unhealthy` when SQL Server cannot be reached. |

```
curl -s -w '\nHTTP %{http_code}\n' http://localhost:8080/health/live
curl -s -w '\nHTTP %{http_code}\n' http://localhost:8080/health/ready
```

```
Healthy
HTTP 200
Healthy
HTTP 200
```

With the database stopped, `ready` returns 503 and `live` stays 200:

```
docker compose stop db
curl -s -w '\nHTTP %{http_code} in %{time_total}s\n' http://localhost:8080/health/ready
docker compose start db
```

```
Unhealthy
HTTP 503 in 16.268414s
```

The first failing check takes about 16 seconds, because it waits for SqlClient's default 15-second connect timeout. For a few seconds after that, failing checks return 503 in a few milliseconds, because SqlClient briefly remembers the failed connection. After `docker compose start db`, `ready` returns 200 again without restarting the API.

Health check requests write no request line: it is logged at Debug, below the minimum level. A failing check is logged at Error. Each `/health/ready` call still logs the `SELECT 1` it runs, as an EF Core SQL line at Information.

## Finding a request in the logs

The API writes one JSON object per line to the console. `@t` is the time, `@mt` the message template, `@l` the level (left out for Information), and `@x` the exception, if there is one. The named properties follow. Each request writes one request line (method, path, status code, elapsed milliseconds), plus any business events, such as a status change, a version conflict or an illegal transition, and the SQL it ran. SQL parameter values are never logged; they show as `?`. Query strings are not logged either.

Every response carries an `X-Correlation-Id` header, and every line logged for that request carries the same value as `CorrelationId`. It is the W3C trace id: if the request sends a `traceparent` header, the id comes from it; otherwise a new one is generated. Error bodies repeat it as `correlationId`.

A stale status change sent with a known `traceparent`:

```
curl -s -i -X POST http://localhost:8080/api/shipments/47826/status \
  -H 'Content-Type: application/json' \
  -H 'traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01' \
  -d '{"status":"InTransit","expectedVersion":0}'
```

```
HTTP/1.1 409 Conflict
Content-Type: application/problem+json; charset=utf-8
Date: Fri, 16 Oct 2026 04:41:51 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Correlation-Id: 4bf92f3577b34da6a3ce929d0e0e4736

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Version conflict","status":409,"detail":"Shipment 47826 is at version 1, not 0. Re-read it and retry.","traceId":"00-4bf92f3577b34da6a3ce929d0e0e4736-9e494b6501579259-01","correlationId":"4bf92f3577b34da6a3ce929d0e0e4736","currentStatus":"PickedUp","currentVersion":1,"allowedTransitions":["InTransit"]}
```

Every log line for that request. `--no-log-prefix` drops compose's `api-1 |` prefix, so each line is plain JSON:

```
docker compose logs --no-log-prefix api | grep 4bf92f3577b34da6a3ce929d0e0e4736
```

```
{"@t":"2026-10-16T04:41:51.9899761Z","@mt":"Executed DbCommand ({elapsed}ms) [Parameters=[{parameters}], CommandType='{commandType}', CommandTimeout='{commandTimeout}']{newLine}{commandText}","@tr":"4bf92f3577b34da6a3ce929d0e0e4736","@sp":"9e494b6501579259","elapsed":"1","parameters":"@__shipmentId_0='?' (DbType = Int32)","commandType":"Text","commandTimeout":30,"newLine":"\n","commandText":"SELECT TOP(2) [s].[Id], [s].[CreatedAt], [s].[CustomerId], [s].[Destination], [s].[DriverId], [s].[Origin], [s].[SearchText], [s].[Status], [s].[TrackingNumber], [s].[UpdatedAt], [s].[Version], [s].[WeightKg]\nFROM [Shipments] AS [s]\nWHERE [s].[Id] = @__shipmentId_0","EventId":{"Id":20101,"Name":"Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted"},"SourceContext":"Microsoft.EntityFrameworkCore.Database.Command","ActionId":"b4fc1db5-41da-4f88-9e99-38e05367c5ba","ActionName":"Dispatch.Api.Controllers.ShipmentsController.ChangeStatus (Dispatch.Api)","RequestId":"0HNPB7U603OTF:00000001","RequestPath":"/api/shipments/47826/status","ConnectionId":"0HNPB7U603OTF","CorrelationId":"4bf92f3577b34da6a3ce929d0e0e4736"}
{"@t":"2026-10-16T04:41:51.9902313Z","@mt":"Version conflict on shipment {ShipmentId}: expected version {ExpectedVersion}, current version {CurrentVersion}","@l":"Warning","@tr":"4bf92f3577b34da6a3ce929d0e0e4736","@sp":"9e494b6501579259","ShipmentId":47826,"ExpectedVersion":0,"CurrentVersion":1,"SourceContext":"Dispatch.Application.Shipments.ShipmentStatusService","ActionId":"b4fc1db5-41da-4f88-9e99-38e05367c5ba","ActionName":"Dispatch.Api.Controllers.ShipmentsController.ChangeStatus (Dispatch.Api)","RequestId":"0HNPB7U603OTF:00000001","RequestPath":"/api/shipments/47826/status","ConnectionId":"0HNPB7U603OTF","CorrelationId":"4bf92f3577b34da6a3ce929d0e0e4736"}
{"@t":"2026-10-16T04:41:51.9904417Z","@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms","@r":["3.0863"],"@tr":"4bf92f3577b34da6a3ce929d0e0e4736","@sp":"9e494b6501579259","RequestMethod":"POST","RequestPath":"/api/shipments/47826/status","StatusCode":409,"Elapsed":3.086347,"SourceContext":"Serilog.AspNetCore.RequestLoggingMiddleware","CorrelationId":"4bf92f3577b34da6a3ce929d0e0e4736","RequestId":"0HNPB7U603OTF:00000001","ConnectionId":"0HNPB7U603OTF"}
```

Log levels are set in the `Serilog` section of `src/Dispatch.Api/appsettings.json`: Information by default, Warning for `Microsoft.AspNetCore`.

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

After loading, the seeder waits until the full-text index covers every shipment, then runs a test search, and only then exits; the API starts after that, so the first search already sees every shipment. On a fresh database the wait took 34 seconds on the development machine (16 CPUs given to Docker). If the index is not complete within 5 minutes, or the test search finds nothing, the seeder exits non-zero and the API does not start.

It is idempotent. If shipments already exist it logs `already seeded, skipping`, repeats the full-text check (immediate once the index is complete), and exits 0, so restarting the stack is safe. There is no reset command. To start over, delete the database volume:

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
