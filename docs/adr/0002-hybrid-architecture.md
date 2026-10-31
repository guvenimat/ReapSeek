# ADR 0002: Rich domain model only where the rules are, not full Clean Architecture

Status: accepted (2026-10-30)

## Context

The features differ a lot in how much business logic they carry. Catalog, search, reviews, customers and most back office pages are queries and simple writes. Cart, orders, payments, returns, coupons and settlement carry invariants whose violation costs money: order state machine, idempotent payment confirmation, per-seller packages and shipping, return windows and quantities, ledger entries.

A uniform Clean Architecture (Domain / Application / Infrastructure project per module, a use-case class per operation, a mediator, generic repositories) would apply the same ceremony to both kinds.

## Decision

- **CRUD-heavy modules:** `*Endpoints` (routes and access, no SQL) → service or pure rules class → `*Repository` (Dapper). Read models are written in SQL, close to the data.
- **Rule-heavy modules:** aggregates, value objects and domain events in `ReapSeek.Domain`, which has no package references (no ASP.NET, Npgsql, Dapper, JSON). An application service owns the transaction: load the aggregate, call one method, save it; domain events go to the outbox in the same transaction.
- No mediator, no generic repository, no separate application project.

The dependency rules (Domain depends on nothing, Shared does not know Api or Worker, endpoints hold no SQL) are checked by architecture tests.

## Consequences

- Business rules are unit tested without a database (`DomainTests.cs`).
- Simple features stay short; there is no mapping layer between a SQL row and a response that adds nothing.
- The style is not uniform, so this ADR and the README explain where each one is used.
