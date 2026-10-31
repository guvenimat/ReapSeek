# ADR 0003: Transactional outbox in PostgreSQL instead of a message broker

Status: accepted (2026-10-30)

## Context

Many changes have side effects outside the database: search indexing, emails, ReapDeep catalog sync and purchase events, cache invalidation, refunds, campaign coupons. Calling those systems inside the request would either lose the side effect when they are down or send it for a change that was later rolled back.

## Decision

The API writes a message to the `outbox` table in the same transaction as the data (`OutboxWriter.EnqueueAsync`). The worker claims messages with `FOR UPDATE SKIP LOCKED` and a lease, handles them by type, retries with exponential backoff and marks them failed after 10 attempts. Failed jobs can be retried or discarded from the back office Jobs page. Each message carries the W3C trace parent, so a trace continues from the request into the worker.

There is no Kafka or RabbitMQ in ReapSeek.

## Consequences

- At-least-once delivery: handlers are idempotent (event ids, `ON CONFLICT`, provider references such as `return-{id}`).
- No extra infrastructure to run; the queue is backed up with the data.
- Throughput is bounded by PostgreSQL, which is far above what a single marketplace instance produces.
