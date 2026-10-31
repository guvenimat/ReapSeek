# ADR 0005: Ports and adapters only at real external boundaries

Status: accepted (2026-11-01)

## Context

Payments, shipping and seller transfers are external providers. Today they are all simulated by `ReapSeek.Sandbox`, but a real marketplace would swap them for real providers, possibly different ones per capability. Before this decision the API, worker, payout runner and simulator each called the Sandbox HTTP API through their own client code.

Interfaces in front of every class (email, search, repositories) would add indirection without a second implementation in sight.

## Decision

`ReapSeek.Shared/Providers/Ports.cs` defines three ports: `IPaymentGateway` (create payment, refund), `ICarrier` (create shipment) and `IPayoutBank` (transfer to a seller). `SandboxProvider` implements all three and is registered once with `AddSandboxProviders()`. Only composition roots (`Infrastructure` namespaces) may reference `SandboxProvider`; this is checked by an architecture test. Inbound webhooks keep their provider-specific verification (`WebhookVerifier`), because their format is part of the provider contract.

Email (Mailpit SMTP), OpenSearch and Redis are not behind ports: they are infrastructure choices, not business partners, and replacing them would mean rewriting the queries anyway.

## Consequences

- Checkout, returns, shipping and payouts depend on the capability, not on the Sandbox.
- Adding a real provider is a new adapter and a registration change.
- The shipping webhook moved into its own `Shipping` module, so returns no longer depend on the seller panel to record a delivery (`PackageDelivery` in Shared does it for both).
