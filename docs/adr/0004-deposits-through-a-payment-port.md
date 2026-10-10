# ADR 0004: Deposits through a payment port, with a fake provider

**Status:** Accepted (October 2026, backend tasks 11 and 12)

## Context

No-shows cost the club money, so confirming a booking takes a deposit. Customers who cancel within the rules get it
back. Payment providers are slow, sometimes down, and retry their webhooks. Their answers can also be lost after the
money moved. The app must never charge twice, and must never keep money for a booking that does not happen. It must also
be testable without a real provider or real money.

## Decision

- **A port, `IPaymentGateway` (charge, refund), with an adapter per provider** (library 4.9 Case 4, Adapter).
  - Results are a closed set: approved, declined with a reason, or no clear answer.
  - Money is `Money`: integer minor units plus an ISO 4217 currency.
  - Today there is one adapter, `FakePaymentGateway`. It is deterministic by test token (`tok_visa`, `tok_declined`,
    `tok_timeout` …) and idempotent per payment id.
- **The flow:**
  1. Booking makes a 10-minute hold.
  2. Confirming charges the deposit, then confirms.
  3. A declined card is 402 and keeps the hold.
  4. No clear answer is 503; a retry reuses the same payment id.
- **Never twice:**
  - The payment's id is the provider's idempotency key on every call.
  - A partial unique index allows one live (pending or paid) payment per booking.
- **Webhooks:**
  - The adapter verifies an HMAC over the raw body, within a 5-minute window, before parsing anything.
  - The event id is recorded with the payment change in one transaction, so a redelivery changes nothing (library
    8.9, 14.2).
- **Refunds follow the cancellation rules:**
  - Whatever may be cancelled (by the customer before the cut-off, by staff at any time) or a paid hold that lapsed is
    refunded in full.
  - The refund is tried at once, and `RefundService` retries what the provider refused.

## Alternatives

- **Provider-hosted checkout (redirect to the provider's page).** Less card data near us, and a fine next step with a
  real provider. As the only design it makes the flow hard to test locally and couples the booking flow to one
  provider's pages.
- **Calling the provider's SDK from services.** Fastest to write. It also spreads the provider's types and errors
  through the code, makes tests need the provider, and locks the club to that provider.
- **Charging when the hold is created.** People who abandon checkout would be charged and refunded constantly.
- **Refunding only inline.** A refund attempted while the provider is down would be forgotten.
- **Webhook work queued for later, as the library describes.** Our work is one local row update, so it runs in the same
  transaction as recording the event. Anything slow (refunds) is left to the job: the acknowledgement stays fast, with
  one less queue.

## Consequences

- **+** Every money path is tested deterministically: declines, timeouts, a hold expiring mid-payment, duplicate and
  forged webhooks, racing confirms.
- **+** A real provider is one adapter (gateway plus webhook reader), with no change to services.
- **−** That adapter still has to map the provider's codes, money format and signature scheme, and those mappings need
  their own tests.
- **−** The deposit is one amount for every booking (`Payments:Deposit`), not per court or time.
- **−** A payment whose outcome stayed unknown is settled by the provider's webhook or a customer retry. Nothing polls
  the provider.
