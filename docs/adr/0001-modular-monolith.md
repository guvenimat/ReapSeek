# ADR 0001: Modular monolith instead of microservices

Status: accepted (2026-10-30)

## Context

ReapSeek is a multi-seller marketplace: catalog, search, cart, checkout, orders, returns, seller settlement, reviews and campaigns. One developer builds and runs it locally. Most flows (checkout, delivery, refunds) change data owned by several features in one step and must stay consistent.

## Decision

One deployable API (`ReapSeek.Api`) and one background worker (`ReapSeek.Worker`), with code organized in feature folders that act as modules. Modules that share invariants form a group (Ordering: carts, checkout, payments, orders, returns, shipping; Catalog: catalog, products, search, media). A repository may only be used inside its own group; other modules go through a service. The rule is enforced by `tests/ReapSeek.UnitTests/ArchitectureTests.cs`.

The API keeps no shared state in process memory, so it can run as several instances (`api-2` in the `scale` profile).

## Consequences

- Cross-feature changes (order delivered → seller ledger) are one PostgreSQL transaction, not a saga.
- One build, one deployment, one place to debug; local startup stays one `docker compose up`.
- The module boundaries are code boundaries only. If a module ever had to be split out, its group already has a single entry point (its services) and its own tables.
- Scaling is horizontal on the stateless API; the worker uses `FOR UPDATE SKIP LOCKED`, so several workers can run too.
