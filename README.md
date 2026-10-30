# ReapSeek

A multi-seller marketplace in the style of large e-commerce sites: a canonical product catalog with variants and competing seller offers, search with facets, cart and checkout, orders split into per-seller packages, payments, shipping, returns, reviews and a seller back office.

ReapSeek is the flagship customer of [ReapDeep](https://github.com/guvenimat/ReapDeep), a multi-tenant product analytics platform. It uses ReapDeep only through its public interfaces, exactly as any other customer would:

- the ReapDeep JS SDK for page views, product views, cart and search events;
- server-side `purchase` and `refund` events from the order pipeline;
- the catalog API to keep ReapDeep's product catalog in sync;
- the ReapDeep Recommender for "similar items", "frequently bought together", popular and trending lists;
- RFM segments and cohorts for coupon targeting and email campaigns;
- ReapDeep alerts, delivered to ReapSeek as signed webhooks.

> Status: planning. The architecture below is the target; implementation proceeds in stages.

## Architecture

| Component | Technology |
|---|---|
| Storefront | Next.js (server-side rendering), TypeScript |
| Back office (admin and sellers) | React, Vite, TypeScript |
| API | .NET 10 Minimal API, Native AOT, modular monolith |
| Background jobs | .NET 10 worker with a transactional outbox |
| Database | PostgreSQL |
| Search | OpenSearch |
| Cache and sessions | Garnet |
| Payments and shipping | `ReapSeek.Sandbox`, a simulated payment provider and carrier |
| Email | Mailpit (local) |
| Analytics and recommendations | ReapDeep |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
