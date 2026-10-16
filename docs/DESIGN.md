# Design Note

## Architecture, and what I deliberately did not do

A layered .NET 8 Web API: Domain (entities and the status transition map, no dependencies), Application (services, DTOs), Infrastructure (the DbContext, migrations, interceptor), Api (thin controllers, middleware), and a separate Seeder console project. Dependencies point inward to achieve structural indirection. Application does reference EF Core: services query through an `IDispatchDbContext` interface that exposes `DbSet<T>` directly. The interface exists to keep the dependency direction inward, so Infrastructure references Application and not the reverse, and it is deliberately not a repository. EF's `DbSet` already is a repository and unit of work, and wrapping it would hide the LINQ the list and search queries are built from. The cost is that Application is not persistence-ignorant; swapping EF out would touch the services, which I judged unlikely enough not to pay for in advance.

The API neither migrates nor seeds on startup. The seeder applies migrations, seeds and exits, and compose starts the API only after it completes successfully.

Not built, on purpose:

- **MediatR, CQRS, a generic repository.** For four endpoints the indirection costs more than it saves.


## Concurrency

Optimistic, using a managed `int` Version incremented by a SaveChanges interceptor, only when a property other than `SearchText` (or `Version` itself) changed. I chose it over `rowversion` mainly for operations: a version of `7` is readable in a support ticket, where base64 binary is not. Provider neutrality is secondary. The cost is that the guarantee lives in application code, not the engine: every update to Shipment must go through the change tracker, and `ExecuteUpdate` or raw SQL would bypass the increment. That is prohibited by convention.

The client sends `expectedVersion`, the version from its last response, in the POST body; a missing value is a 400. The handler compares it with the loaded row first and returns 409 on mismatch, before validating the transition, so a stale client learns the shipment moved instead of getting a misleading "illegal transition". EF also checks the version in the UPDATE's WHERE clause, which closes the race between load and save. The status change and its history row are written in one SaveChanges call, so a failed check rolls back both. A 409 carries the current status, current version and the transitions allowed from there.

## Search and indexes

Free-text search runs on a denormalised `SearchText` column (tracking number, customer name, destination) with one full-text index, because an index covers one table and these fields span two.

Why full-text instead of a regular B-tree index: a B-tree keeps values sorted by their leading characters, so it can only seek when the term is at the start of the stored value (LIKE 'term%'). Users search for a word anywhere in the text: a surname after a first name, a city inside a destination, one segment of a tracking number. For that, LIKE '%term%' cannot use the index order, so SQL Server reads every row and tests the pattern against each one, and the cost grows with the table, not with the number of matches. Indexing the three columns separately does not change this. A full-text index is an inverted index: it maps each word to the rows that contain it, so a word prefix anywhere in the text is found by lookup rather than by testing every row, and two terms become an intersection of two match lists.

## Observability, and the 2am case

The API logs structured JSON (Serilog compact JSON) with named properties.

I did not ship a monitoring service. In production, a monitoring service running outside this stack would alert by email on fatal issues: technical ones such as the readiness check failing repeatedly, a sustained rate of 5xx errors or SQL Server becoming unreachable, and business ones such as shipments stuck in a status far longer than expected.

I would also route each alert to an AI agent with read-only access to the logs and health data. It would start investigating as soon as the alert fires, use the correlation ids to pull the failing requests, and write up what failed, since when and the likely cause, without taking any action. When I come online, I start from that summary instead of a bare alert.

## Serving 50 pharmacies with strict isolation

Add multi-tenancy logic that maps each request to its pharmacy’s database, using the tenant ID from the authenticated user’s token (AuthN & AuthZ must be implemented first).