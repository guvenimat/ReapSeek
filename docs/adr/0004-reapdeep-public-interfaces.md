# ADR 0004: ReapDeep is used only through its public interfaces

Status: accepted (2026-10-30)

## Context

ReapDeep (behaviour analytics, recommendations, cohorts, alerts) is a sibling project that runs Kafka, StarRocks and PostgreSQL. ReapSeek could read those stores directly, which would be faster to build.

## Decision

ReapSeek uses ReapDeep like any other customer would: the JS SDK and Ingestion API for events, the catalog API (`ck_` key) for items, the Recommender for recommendations, the Query API (`rk_` key) for cohorts and customer profiles, and signed webhooks for alerts. ReapSeek never touches ReapDeep's databases or topics. When something is missing, it is added to ReapDeep as a feature.

The ReapDeep `item_id` is the ReapSeek product id; seller and variant go as event properties. Only `customer-{id}` identifies a person; no personal data is sent.

## Consequences

- Either project can change its storage without breaking the other.
- ReapSeek exercises ReapDeep's public API end to end, which is how gaps in it were found.
- ReapDeep being down never breaks shopping: recommendations fall back to cached or catalog lists, audience reads fail the campaign with a visible error.
