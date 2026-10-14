# Shipment Dispatch Service

Backend for a courier dispatch console: .NET 8 Web API, SQL Server, EF Core.

The only prerequisite is Docker with Compose v2. No .NET SDK, SQL Server, or configuration is needed.

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
