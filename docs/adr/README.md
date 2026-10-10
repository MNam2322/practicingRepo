# Architecture decision records

One page per decision that shapes the system: what was chosen, what else was considered, and what it costs. The code
shows *what* was done; these say *why*, so a settled question is not argued again, and one whose context has really
changed can be reopened knowingly (library 14.6 "Architecture Decision Records").

| # | Decision | Status |
|---|---|---|
| [0001](0001-postgresql-in-production-sqlite-in-development.md) | PostgreSQL in production, SQLite in development, one EF Core model | Accepted |
| [0002](0002-the-database-refuses-overlapping-bookings.md) | The database refuses overlapping bookings | Accepted |
| [0003](0003-aspnet-core-identity-with-a-same-site-cookie.md) | ASP.NET Core Identity with an HttpOnly same-site cookie | Accepted |
| [0004](0004-deposits-through-a-payment-port.md) | Deposits through a payment port, with a fake provider | Accepted |
| [0005](0005-controllers-services-repositories.md) | Controllers → services → repositories, enforced by tests | Accepted |

## Writing a new one

Copy the shape of an existing record: **Status** (Proposed, Accepted, or Superseded by 00NN), **Context** (the forces,
in plain words), **Decision**, **Alternatives** (each with why it lost), **Consequences** (good and bad, honestly). Keep
it to a page. When a decision changes, write a new record that supersedes the old one; do not rewrite history.
