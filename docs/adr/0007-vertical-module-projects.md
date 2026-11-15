# ADR 0007: One vertical project pair per module

Status: accepted (2026-11-15), being implemented in phases. Refines ADR 0001 and ADR 0006.

## Context

Projects are horizontal: `ReapSeek.Api` (endpoints, services and repositories per module folder), `ReapSeek.Modules` (shared module persistence and the public APIs modules use to reach each other), `ReapSeek.Domain`, `ReapSeek.Shared` (infrastructure) and `ReapSeek.Worker`. One module's code is spread over four or five projects.

Module boundaries are enforced only by tests (the `PublicApi` whitelist in `ArchitectureTests` and the cross-schema SQL scan in `ModuleSqlTests`), not by the compiler or the database. Cross-module APIs take `NpgsqlConnection` / `NpgsqlTransaction`, and several flows share one transaction across modules: checkout reserves catalog stock and redeems a marketing coupon, sellers writes the catalog's seller snapshot, ordering writes the finance ledger on delivery. None of this can cross a network, so extracting a module would mean rewriting those flows.

## Decision

Each module becomes two projects:

```
src/
  BuildingBlocks/
    ReapSeek.BuildingBlocks        database, outbox/inbox, Redis, telemetry, validation helpers, money
    ReapSeek.BuildingBlocks.Web    API results, error handling, UserInfo and Require*() access metadata, rate limiting
  Modules/<Module>/
    ReapSeek.<Module>.Contracts    public: I<Module>Api, DTOs, versioned integration events; no dependencies
    ReapSeek.<Module>              everything internal except <Module>Module.cs (Add<Module>(), Map<Module>Endpoints(), Add<Module>Jobs())
      Domain/ Application/ Infrastructure/ Api/ Integration/ Jobs/ Migrations/
  Hosts/
    ReapSeek.Api                   composition root, plus thin BFF endpoints that only call Contracts
    ReapSeek.Worker                composition root for module jobs and inbox handlers
  ReapSeek.Sandbox, ReapSeek.Simulator
```

Modules: Identity, Catalog, Sellers, Ordering, Finance, Engagement, Marketing, plus Notifications (all e-mails and templates; listens to events only, so no Contracts project) and Insights (everything that talks to ReapDeep: catalog sync, event ingestion, recommendations, cohorts). That is 24 projects including Sandbox, Simulator and the unit tests; a project per layer would be 43 with little gain.

Rules:

- Dependencies: Hosts → module → other modules' `.Contracts` only, and BuildingBlocks. Inside a module: Api → Application → Domain; Infrastructure → Application, Domain. Module types are internal, so the compiler enforces the boundary. Each module gets its own database role and connection limited to its own schema, so the database enforces it too; the global search_path goes away.
- Commands go endpoint → use case → domain + port; reads go endpoint → query class (SQL → DTO). Still no MediatR or generic repository, and the hybrid rule of ADR 0002 stays: a rich domain only where there are rules, CRUD modules may have an empty Domain.
- Synchronous calls go through Contracts interfaces that take no connection or transaction. In the monolith the implementation is in-process; after extraction an HTTP client implements the same interface.
- Asynchronous: every module has its own `outbox` and `inbox` table in its schema. A module writes a versioned event (e.g. `OrderPaidV1`) to its outbox in the same transaction as its data, a relay copies it to each subscriber's inbox, and each subscriber processes its inbox with its own retry counter. On extraction only the relay changes (it publishes to a broker).
- Identity: modules know only `UserInfo` from BuildingBlocks.Web; session validation is middleware provided by Identity. After extraction a gateway would validate and forward a signed identity.

Flows that share a transaction today become reservation plus events:

| Flow | New shape |
|---|---|
| Checkout stock | `ICatalogApi.ReserveStock(orderId, lines)`, idempotent on orderId, in the catalog's own transaction before the order is written; released if the order fails, a reconciliation job cleans up leftovers |
| Coupon | `IMarketingApi.RedeemCoupon(coupon, orderId)`; released on `OrderCancelled` |
| Stock return on cancel | catalog listens to `OrderCancelled` / `PackageCancelled` |
| Seller snapshot | sellers publishes `SellerChangedV1`, catalog updates its own copy |
| Ledger | finance builds entries from `PackageDelivered` and `ReturnRefunded` |
| Ratings | engagement publishes `ReviewChangedV1`; catalog and sellers keep their own aggregates |

Target synchronous dependencies: Ordering → Catalog, Marketing, Sellers; Engagement → Catalog, Ordering, Identity; Marketing → Identity, Insights; Insights → Catalog, Ordering; Notifications → Identity; Catalog, Identity, Sellers, Finance → none (events only).

Migrations: the existing 21 migrations are squashed into one baseline per module and the local database is reset (there is no production). The resulting schema is checked against a `pg_dump` of the old chain.

Phases: (1) spike on Engagement; (2) building blocks and per-module outbox, inbox, roles and migrations; (3) move modules without behaviour change (Engagement, Marketing, Finance, Sellers, Identity, Catalog, Ordering); (4) replace shared transactions with reservation and events; (5) Notifications, Insights and BFF endpoints; (6) remove the old projects, update architecture tests and README. As proof, Catalog is then run as a separate process (same code, HTTP clients behind the Contracts interfaces) and documented in the README.

Alternatives considered: keeping the shared transaction (less work, but checkout must be rewritten on extraction); one project per layer per module (43 projects); keeping the old migration chain and adding per-module migrations after it.

## Consequences

- Extracting a module becomes adding a host and swapping in HTTP clients, not rewriting flows.
- Checkout is no longer one PostgreSQL transaction. Stock is reserved atomically first, so there is no overselling, but "stock reserved, order not created" can exist briefly until reconciliation.
- Eventual consistency: a suspended seller leaves the buy box about a second later instead of immediately, and some end-to-end tests become wait-and-check.
- This supersedes the parts of ADR 0006 that allowed calling another module's API inside the caller's transaction and writing another module's read model in the same transaction.
- Native AOT risk: the Minimal API request delegate generator, System.Text.Json source generation and Dapper.AOT must work with internal types inside class libraries. The Engagement spike verifies this first; if endpoints cannot live in the module library, endpoint mapping stays in the host and the rest of this decision is unchanged.
- More projects (8 → 24) and a larger migration, done in the phases above.
- Unchanged: URLs, storefront, back office, Native AOT, Dapper.AOT, two images (API and Worker), the Helm chart.
