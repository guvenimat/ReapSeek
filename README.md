# ReapSeek

A multi-seller marketplace in the style of large e-commerce sites: a canonical product catalog with variants and competing seller offers, search with facets, cart and checkout, orders split into per-seller packages, payments, shipping, returns, reviews and a seller back office.

ReapSeek is the flagship customer of [ReapDeep](https://github.com/guvenimat/ReapDeep), a multi-tenant product analytics platform. It uses ReapDeep only through its public interfaces, exactly as any other customer would:

- the ReapDeep JS SDK for page views, product views, cart and search events;
- server-side `purchase` and `refund` events from the order pipeline;
- the catalog API to keep ReapDeep's product catalog in sync;
- the ReapDeep Recommender for "similar items", "frequently bought together", popular and trending lists;
- RFM segments and cohorts for coupon targeting and email campaigns;
- ReapDeep alerts, delivered to ReapSeek as signed webhooks.

> Status: the foundation is in place (accounts, sessions, outbox, storefront and back office shells). The catalog, search, checkout and the ReapDeep integration follow in stages.

![ReapSeek storefront home page](docs/images/storefront-home.png)

## Architecture

| Component | Technology |
|---|---|
| Storefront | Next.js (server-side rendering), TypeScript |
| Back office (admin and sellers) | React, Vite, TypeScript |
| API | .NET 10 Minimal API, Native AOT, modular monolith |
| Background jobs | .NET 10 worker with a transactional outbox |
| Database | PostgreSQL, versioned SQL migrations |
| Search | OpenSearch |
| Cache | Garnet |
| Payments and shipping | `ReapSeek.Sandbox`, a simulated payment provider and carrier |
| Email | Mailpit (local) |
| Analytics and recommendations | ReapDeep |

### What is built so far

- **Accounts:** customer registration and sign-in, PBKDF2 password hashing, password change that signs out other devices, rate-limited sign-in.
- **Separate sessions for the storefront and the back office:** different cookies (`rs_session` with `SameSite=Lax` for 30 days, `rs_bo_session` with `SameSite=Strict` for 12 hours), so a customer session never opens the back office.
- **Transactional outbox:** the API writes domain messages in the same transaction as the data; the worker claims them with `FOR UPDATE SKIP LOCKED` and a lease, retries with exponential backoff and gives up after 10 attempts. The first consumer sends the welcome email.
- **Migrations:** SQL files embedded in `ReapSeek.Shared`, applied in order under an advisory lock by a one-shot `migrate` container before the API and the worker start.
- **Storefront:** server-rendered header with the signed-in customer, category navigation, home and category pages, sign-in, registration and account pages.
- **Back office:** staff sign-in and an overview with live API status.

![Back office overview](docs/images/backoffice-overview.png)

## Repository layout

```
src/
  ReapSeek.Api/        Minimal API (feature folders: Identity, Health, ...)
  ReapSeek.Worker/     Outbox processor and notifications
  ReapSeek.Shared/     Migrations, outbox contracts, validation helpers
storefront/            Next.js storefront
backoffice/            React back office
deploy/local/          Docker Compose stack and end-to-end tests
tests/                 Unit tests
```

## Running locally

Requirements: Docker Desktop, PowerShell (Windows) and, for development outside Docker, the .NET 10 SDK and Node.js 22+.

```powershell
cd deploy/local
.\run-local.ps1
```

The script creates `deploy/local/.env` with random passwords on the first run, builds the images and starts the stack.

| Service | URL |
|---|---|
| Storefront | http://localhost:3100 |
| Back office | http://localhost:3101 (admin credentials are in `deploy/local/.env`) |
| API | http://localhost:8100 |
| Mailpit | http://localhost:8025 |

## Tests

```powershell
dotnet test tests/ReapSeek.UnitTests
cd deploy/local
.\auth-test.ps1
```

`auth-test.ps1` runs end to end against the running stack: registration, sign-in, session separation, password change, the welcome email arriving in Mailpit through the outbox, server-rendered storefront pages and the sign-in rate limit.

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
