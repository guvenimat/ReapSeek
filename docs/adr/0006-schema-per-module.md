# ADR 0006: One database schema per module

Status: accepted (2026-11-14), being implemented in phases. Refines ADR 0001.

## Context

ADR 0001 made feature folders the modules, but the boundaries stayed code-only. All tables live in one schema, modules join each other's tables in SQL (the buy box query exists in five places), and `ReapSeek.Shared` mixed infrastructure with order, return, ledger and coupon persistence so the API, worker and simulator could reuse it. Nothing stopped a module from reading another module's tables.

## Decision

Each module owns one PostgreSQL schema in the same database:

| Module | Schema | Tables |
|---|---|---|
| Identity | `identity` | users, sessions, account_tokens, addresses |
| Catalog | `catalog` | categories, brands, products, product_images, product_variants, offers, buy_box |
| Sellers | `sellers` | sellers |
| Ordering | `ordering` | carts, cart_lines, orders, order_packages, order_lines, payments, payment_events, shipment_events, returns, return_lines |
| Finance | `finance` | seller_ledger, payouts |
| Engagement | `engagement` | reviews, questions, answers, wishlist_items |
| Marketing | `marketing` | campaigns, coupons, reapdeep_alert_subscriptions, analytics_alerts |
| Platform | `platform` | outbox |

Rules:

- A module's SQL touches only its own schema. There are no foreign keys across schemas; other modules' rows are referenced by id.
- Commands that must be atomic across modules (reserving stock at checkout, redeeming a coupon, crediting the seller ledger on delivery) call the owning module's public API inside the caller's transaction. The database is shared, so this stays one PostgreSQL transaction instead of a saga.
- Reads across modules go through the owning module's public API, or, when they are hot or need joins (the buy box needs seller status and rating), through a local read model kept current by outbox events.
- Module code that the API, worker and simulator all need lives in `ReapSeek.Modules`; `ReapSeek.Shared` holds infrastructure and external adapters only and does not reference the domain.
- Architecture tests enforce the dependency rules and scan module SQL for other modules' schemas.

Phases: (1) this ADR and `ReapSeek.Modules`, Shared becomes infrastructure only; (2) module folders with public APIs; (3) tables move into their schemas; (4) cross-module joins are replaced module by module; (5) cross-schema foreign keys are dropped and the SQL scan is switched on; (6) worker, simulator, seed and docs.

## Consequences

- Module boundaries become data boundaries, so a module could be extracted into a service by swapping in-transaction calls for messages (and a saga where atomicity is needed).
- Some queries get more complex: lists that joined across modules either call another module or read a projection, and projections are eventually consistent.
- Cross-module atomicity stays cheap only while the modules share one database; that coupling is deliberate and documented here.
