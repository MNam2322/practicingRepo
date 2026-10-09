---
id: "10-6"
number: "10.6"
title: "Privacy, compliance and the supply chain"
part: 10
part_title: "Security"
words: 5583
minutes: 24
---

# 10.6 Privacy, compliance and the supply chain

> **Quick look.** Personal data, compliance and the software supply chain.
>
> - Don't collect what you don't need; isolate PII in one place so erasure touches one table.
> - Publish ids, not names and emails, in events; plan account deletion across caches, logs, backups and search.
> - Set retention on logs, traces, backups and topics; residency is an architectural constraint.
> - The app should never connect as the database owner; use least-privilege roles and row-level security for tenants.
> - Disk encryption protects files, not data from a compromised app credential.
> - Supply chain: lock files, vulnerable-package build gates, secret scanning, SBOMs, signed images.
> - The boring controls (MFA, patches, least privilege, no standing access, immutable backups) prevent most breaches.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: "Delete my account" across everything

A customer requests erasure. Their data sits in the database, Redis, search index, logs, backups and analytics.

*Crypto-shredding for data you can't easily delete*

```csharp
// each customer's personal fields are encrypted with their own data key, stored once:
var key = await keys.GetOrCreateAsync(customerId, ct);
row.PassportEnc = Encrypt(key, passportNumber);

// erasure: delete the key; every copy (including backups) becomes unreadable
await keys.DeleteAsync(customerId, ct);
await search.DeleteAsync($"customer-{customerId}", ct);
await cache.RemoveByTagAsync($"customer:{customerId}", ct);
```

> **Lesson:** Design for erasure: isolate personal data, keep an inventory of copies, and use per-person keys for data you can't chase down.

#### Example 2: A vulnerable transitive package

A security bulletin names a JSON library you never referenced directly.

*Find and gate it*

```bash
dotnet list package --vulnerable --include-transitive
# pin a fixed version centrally (Directory.Packages.props) even though it's transitive:
#   <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
#   <PackageVersion Include="Vulnerable.Lib" Version="5.0.3" />
# CI gate: fail the build when high/critical vulnerabilities are present
dotnet restore --locked-mode
```

> **Lesson:** Your attack surface includes every transitive package. Audit them in CI and pin fixes centrally.

### Data Privacy, Retention & Deletion

<p class="src">From <em>.NET Backend Deep Dive</em> · Data Privacy, Retention & Deletion</p>

<a id="dn-220"></a>

#### "Delete my account" versus everything you've built

Regulation aside, this is an architecture problem — and it collides directly with several patterns earlier in this document.

A deletion request has to reach data you've deliberately scattered:

| Location | Problem |
|---|---|
| Main table | **Soft delete (Part 2.1) doesn't delete anything.** `IsDeleted = 1` is not erasure |
| Outbox / event log | Events carry a copy of the payload, sometimes forever |
| Kafka topics | Retention is time-based; the record persists until it ages out or is compacted |
| Read models / caches | Redis, search indexes, denormalized projections |
| Backups | You are not going to rewrite last month's backup file |
| Logs and traces | The one nobody remembers |
| Analytics warehouse | Copied and aggregated long ago |
| Third parties | Payment processor, email provider, CRM |

The workable approach is **crypto-shredding**: encrypt each user's personal data with a per-user key, and to erase them, destroy the key. The ciphertext in backups and event logs becomes permanently unreadable without rewriting history. It's the only technique that answers the backup problem honestly.

Otherwise: hard-delete or anonymize in the primary store, tombstone the event stream (Kafka compaction with a null value for the key), purge caches and indexes, and document a defensible retention window for backups.

<a id="dn-221"></a>

#### Design so the problem is small

- **Don't collect what you don't need.** The cheapest compliance measure in existence.
- **Isolate PII** into one table or service, referenced by id elsewhere. Then erasure touches one place rather than forty.
- **Pseudonymize in events.** Publish `CustomerId`, not name and email — consumers can look it up if they're entitled to. This also makes your events smaller and more stable.
- **Never log PII** (Part 1.4). Logs are replicated, shipped, retained and searched by more people than any database.
- **Anonymize rather than delete** where you need the aggregate: keep the order row for accounting, replace the identity with a tombstone. Note that legal obligations to *retain* invoices routinely conflict with requests to *erase* — which is why erasure is usually "anonymize the person, keep the transaction."

<a id="dn-222"></a>

#### Retention as a first-class feature

```sql
-- Every table holding personal data needs an answer to "for how long?"
-- and a job that enforces it, batched (Part 15.5)
DELETE TOP (5000) FROM AuditLog WHERE CreatedAt < DATEADD(year, -2, GETUTCDATE());
```

Set retention on logs, traces, backups, S3 buckets (lifecycle policies) and Kafka topics. "Keep everything forever" is simultaneously a liability, a cost line (Part 48.1), and a breach-impact multiplier.

<a id="dn-223"></a>

#### Data residency and transfers

If users are in a region with residency requirements, the region is an architectural constraint, not a deployment detail — it affects where you put databases, backups, logs *and* your observability vendor. Note that shipping traces to a US-hosted SaaS is a data transfer too; it's the one teams most often overlook.

---

### CI/CD & Delivery

<p class="src">From <em>.NET Backend Deep Dive</em> · CI/CD & Delivery</p>

<a id="dn-189"></a>

#### Supply chain

- **Pin dependencies**; commit the lock file. Enable Dependabot/Renovate but review transitive bumps.
- `dotnet list package --vulnerable --include-transitive` as a build gate.
- **Secret scanning on commits**, not just in review — and treat any leaked credential as compromised and rotate it, even if the commit was amended away.
- Generate an **SBOM** if you ship software to anyone who will eventually ask for one.

---

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a189"></a>

#### Personal data: classification, redaction and retention

Most .NET security material stops at authentication. The obligations that actually reach production — and the ones auditors ask about — are about the data itself: what you hold, who can see it, how long you keep it, and whether it leaks into places nobody thinks of as a database. Logs, traces, caches, queues, backups and support exports all count.

| Class     | Examples                        | Handling                                                             |
|-----------|---------------------------------|----------------------------------------------------------------------|
| Public    | Product catalogue, docs         | No constraints                                                       |
| Internal  | Order counts, aggregate metrics | Authenticated access                                                 |
| Personal  | Name, email, address, IP        | Access-controlled, redacted in logs, deletable on request            |
| Sensitive | Health, payment, government ID  | All of the above plus encryption at rest, narrow access, audit trail |

``` csharp
// 1. Mark it in the model — classification you can act on in code
public class Customer
{
    public Guid Id { get; set; }
    [PersonalData] public string Email { get; set; } = "";
    [PersonalData] public string FullName { get; set; } = "";
    [SensitiveData] public string? NationalId { get; set; }
}

// 2. Keep it out of logs by construction, not by remembering
public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderId} placed for customer {CustomerId}")]
    public static partial void OrderPlaced(ILogger l, Guid orderId, Guid customerId);
}
// log ids, never the person. If a payload must be logged for support,
// redact first — and make the redactor a shared, tested component:
public static string RedactEmail(string email) =>
    Regex.Replace(email, @"(?<=.).(?=[^@]*?@)", "*");   // j***@corp.com

// 3. Encrypt sensitive columns — EF value converter + Data Protection
b.Entity<Customer>().Property(c => c.NationalId)
    .HasConversion(v => protector.Protect(v!), v => protector.Unprotect(v));
```

###### Deletion is harder than it looks

A “delete my account” request has to reach every copy: the row, the read replicas, the search index, the cache, the message broker’s retained events, the analytics warehouse, and yesterday’s backup. Backups are the usual honest answer — you cannot surgically edit them, so the accepted practice is a documented retention window after which they expire. Design for it by keeping personal data in as few tables as possible: a *Customer* row that other tables reference by id is deletable; personal fields copied into ten denormalised tables are not.

> **\[Trap\]**
>
> **Common trap.** The quiet leaks are the ones outside the database: an exception message containing a full request body, a trace attribute carrying an email, a support export sitting in a shared drive, a queue whose messages retain personal data long after the row was deleted. Audit those paths deliberately — nobody finds them by reading the schema.

> **\[Why it matters\]** Data classification also simplifies engineering decisions. Once a field is labelled sensitive, the questions answer themselves: it is encrypted, excluded from logs, restricted in access, and included in the deletion path. Without labels, every one of those decisions is relitigated per feature, and the one that gets forgotten is the one that ends up in an incident report.

### Evolving, testing, securing data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Evolving, testing, securing data</p>

<a id="be-m227"></a>

#### Handling sensitive data

**Injection.** Parameterise, always. There is no legitimate use of string concatenation to build SQL — not for a column name, not for a sort direction, not "just this once" ([Raw SQL, FromSql, and stored procedures](07-07-orms-and-ef-core.md#be-m167)).

``` csharp
// Parameters cannot express identifiers, so validate against an allow-list instead:
private static readonly FrozenDictionary<string, string> SortColumns =
    new Dictionary<string, string> {
        ["date"] = "o.CreatedAt", ["total"] = "o.Total", ["id"] = "o.Id"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

if (!SortColumns.TryGetValue(request.SortBy, out var column))
    throw new ArgumentException("Unknown sort column");
var dir = request.Descending ? "DESC" : "ASC";
var sql = $"SELECT ... ORDER BY {column} {dir}";   // both values came from our own table
```

**Passwords are hashed, never encrypted** — encryption is reversible, and that is exactly what you do not want.

``` csharp
// Use a deliberately slow, memory-hard KDF with a per-user salt.
// ASP.NET Core Identity's PasswordHasher is the right default; if hand-rolling:
var salt = RandomNumberGenerator.GetBytes(16);
var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations: 600_000,
                                      HashAlgorithmName.SHA256, outputLength: 32);
// Compare in constant time - a plain == leaks timing information:
CryptographicOperations.FixedTimeEquals(hash, stored);

// NEVER: MD5, SHA1, or a bare SHA256 of a password. All are far too fast.
```

**Encryption at rest** operates at three levels, and they protect against different things: *TDE* protects the files (a stolen backup), *column encryption* protects the column from anyone with database access, and *application-level encryption* protects it from the database administrator too.

``` csharp
// Application-level, with authenticated encryption. Never plain AES-CBC without a MAC.
using var aes = new AesGcm(key, tagSizeInBytes: 16);
var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
// ... and store nonce + tag + ciphertext together; a reused nonce breaks GCM entirely.
```

> **\[Trap\]**
>
> **Encrypted columns cannot be searched, sorted, or indexed meaningfully** — deterministic encryption allows equality lookups but leaks which rows share a value, which for something like a national ID is often enough to identify people. Decide per column what you actually need: sometimes a keyed hash for lookup plus separately encrypted storage is the right shape.

> **\[Trap\]**
>
> **Logs are the most common leak.** `EnableSensitiveDataLogging()` puts every parameter value into your logs — it is a development-only setting and it must never reach production config. Structured logging with message templates makes redaction possible; string interpolation into a log message makes it impossible. Audit what your exception handler serialises, too: a `DbUpdateException` can carry entity values straight into a stack trace.

> **\[Tip\]**
>
> **Minimise, then protect.** The cheapest way to secure a field is not to store it. Truncate what you can (last four digits), tokenise what you must keep but rarely read, and set a retention policy on everything — data you deleted last year cannot be breached this year ([Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)).

Worked example: [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)

First principles: [TLS: what the handshake establishes](06-05-tls.md#be-m17)

Applied in: [Logging that is actually queryable](11-06-observability.md#be-a60)

### Security & Governance

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Security & Governance</p>

<p class="pd">Who may see what, proved rather than assumed. Privileges, encryption, row-level security, injection, and the auditing and retention obligations that come with holding other people&#x27;s data.</p>

<a id="be-d468"></a>

#### Authentication, roles and least privilege

The application connecting as the database owner is the single most common database security failure. It means any SQL injection, any compromised credential and any mistaken migration has unrestricted power.

``` text
  a role hierarchy that actually limits damage

  db_owner            (DDL only; used by migrations, never by the application)
    |
  app_rw              SELECT, INSERT, UPDATE, DELETE on application tables
    |                 NO DDL. NO access to audit tables. NO superuser.
  app_ro              SELECT only  -> used by reporting and by read replicas
    |
  analyst             SELECT on VIEWS only, never on base tables

  and separately:
  migrator            DDL, run only by the pipeline, credentials not in the app
```

``` sql
-- lock down the default, then grant deliberately
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;      -- PostgreSQL 15+ does this by default

CREATE ROLE app_rw NOLOGIN;
GRANT USAGE ON SCHEMA app TO app_rw;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA app TO app_rw;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA app TO app_rw;

-- and for tables created LATER, which people always forget
ALTER DEFAULT PRIVILEGES IN SCHEMA app
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_rw;

-- the login role inherits from the group role
CREATE ROLE app_service LOGIN IN ROLE app_rw;

-- deny the dangerous things explicitly
REVOKE DELETE ON app.audit_log FROM app_rw;      -- append-only by design
```

| Principle                        | Concretely                                                                     |
|----------------------------------|--------------------------------------------------------------------------------|
| Separate DDL from DML            | The application cannot alter the schema; migrations use a different credential |
| Group roles, not per-user grants | Grant to a role; add and remove members                                        |
| No superuser for applications    | A superuser bypasses row-level security and every permission check             |
| Read-only where possible         | Reporting, exports, replicas                                                   |
| Passwordless where possible      | Managed identity, IAM auth, certificates — no secret to leak or rotate         |
| Time-bound elevated access       | Break-glass credentials that expire, and that are logged                       |

> **\[Note\]**
>
> **The single highest-value change.** Give the application a role that cannot execute DDL and cannot delete from audit tables. It costs an hour, and it converts several classes of catastrophic incident — a dropped table via injection, a runaway migration, an attacker covering their tracks — into a permission error.

<a id="be-d469"></a>

#### Encryption at rest and in transit

| Layer                                 | Protects against                               | Does not protect against                               |
|---------------------------------------|------------------------------------------------|--------------------------------------------------------|
| **TLS in transit**                    | Network interception, machine-in-the-middle    | Anything at either endpoint                            |
| **Full-disk / volume encryption**     | Stolen disks, decommissioned hardware          | Anyone with database access                            |
| **TDE** (transparent data encryption) | Stolen data files and backups                  | Any authenticated query — it is transparent            |
| **Column-level encryption**           | A database compromise, and privileged insiders | Requires key management; breaks indexing and searching |
| **Application-side encryption**       | Everything below the application               | You lose all query capability on that column           |

The distinction that matters: TDE and disk encryption protect the *files*. They do nothing against a compromised application credential, because a legitimate query decrypts transparently. Protecting data from someone who has database access requires column-level encryption or row-level security, and both have real costs.

``` text
-- enforce TLS, and verify the certificate. 'require' encrypts but does not
-- authenticate the server, so it does not stop a machine-in-the-middle.
sslmode=verify-full sslrootcert=/etc/ssl/certs/ca.crt

-- and refuse non-TLS connections at the server
# pg_hba.conf
hostssl  all  all  0.0.0.0/0  scram-sha-256
host     all  all  0.0.0.0/0  reject
```

``` sql
-- deterministic vs randomised encryption: the trade is searchability
-- deterministic: the same plaintext always yields the same ciphertext
--   -> equality search and joins work; frequency analysis becomes possible
-- randomised:    the same plaintext yields different ciphertext each time
--   -> stronger; no equality search, no index seek, no join

-- a common compromise: store a searchable BLIND INDEX alongside the ciphertext
CREATE TABLE patients (
    id          bigint PRIMARY KEY,
    ssn_cipher  bytea NOT NULL,       -- randomised encryption, application-side
    ssn_hmac    bytea NOT NULL,       -- HMAC(key, normalised ssn) - deterministic
    ...
);
CREATE INDEX ix_patients_ssn_hmac ON patients (ssn_hmac);
-- lookup: compute the HMAC of the search term, seek the index, then decrypt.
-- the HMAC key must be separate from the encryption key.
```

> **\[Trap\]**
>
> **Common trap.** Encrypting a column removes every capability that depends on its value: index seeks, range queries, sorting, joins, `LIKE`, uniqueness constraints and foreign keys. Decide what you need to query *before* encrypting, not after — retrofitting a blind index across an existing dataset requires reading and rewriting every row.

<a id="be-d470"></a>

#### Row-level security and data masking

Instead of every query remembering to filter by tenant, the database enforces it. This is the strongest available defence against a multi-tenant data leak, because it holds even for raw SQL, a forgotten `WHERE`, or an injected query.

``` sql
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE orders FORCE ROW LEVEL SECURITY;     -- applies to the table owner too

-- readers see only their tenant
CREATE POLICY tenant_read ON orders FOR SELECT
    USING (tenant_id = current_setting('app.tenant_id')::uuid);

-- writers cannot write outside it either
CREATE POLICY tenant_write ON orders FOR ALL
    USING      (tenant_id = current_setting('app.tenant_id')::uuid)   -- rows I may touch
    WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);  -- rows I may create

-- the application sets the context once per transaction, from a VERIFIED claim
SET LOCAL app.tenant_id = '3f2a...';    -- LOCAL: reset at commit, safe with pooling
```

``` text
  why this is stronger than an application filter

  application filter    every query must remember. One forgotten WHERE = a breach.
                        raw SQL, reports and migrations bypass it entirely.

  row-level security    the database rewrites EVERY query to add the predicate.
                        a forgotten WHERE returns only permitted rows.
                        raw SQL is filtered. An injected query is filtered.

  the remaining holes:  a SUPERUSER connection bypasses RLS
                        BYPASSRLS role attribute
                        the table owner, unless you use FORCE
```

| Feature                | Purpose                                  | Caveat                                             |
|------------------------|------------------------------------------|----------------------------------------------------|
| Row-level security     | Restrict which rows are visible          | Adds a predicate to every plan — index it          |
| Column privileges      | `GRANT SELECT (col) `                    | Coarse; per-role, not per-row                      |
| Dynamic data masking   | Show `xxxx1234` to unprivileged roles    | Obfuscation, not security — inference attacks work |
| Views with predicates  | A poor-man's RLS                         | Bypassed by querying the base table                |
| Security-barrier views | Prevents predicate pushdown leaking data | Use with `security_barrier = true`                 |

> **\[Trap\]**
>
> **Common trap.** `SET` without `LOCAL` persists for the connection, and with a transaction-mode pooler that connection is handed to the next request — which then reads the previous tenant's data. Always `SET LOCAL` inside a transaction, and add a test that proves a pooled connection cannot leak context across requests.

<a id="be-d472"></a>

#### Auditing and compliance

Knowing who did what, provably, and being able to show it. This is usually a regulatory obligation rather than an engineering preference, and it is easier to build in than to retrofit.

| Mechanism                    | Captures                                      | Cost                                            |
|------------------------------|-----------------------------------------------|-------------------------------------------------|
| Statement logging            | Every statement, with the role                | Very high volume; expensive to store and search |
| Extension-based audit        | Configurable: DDL, writes, or specific tables | Moderate — `pgaudit`, SQL Server Audit          |
| Application audit table      | Business-level events with user context       | Cheap and the most useful for investigations    |
| Triggers on sensitive tables | Row-level before and after values             | Write overhead; complete coverage               |
| Temporal tables              | Full row history automatically                | Storage growth                                  |
| CDC into an audit store      | Every change, off the transactional path      | Infrastructure                                  |

``` sql
-- an append-only audit table the application role cannot alter
CREATE TABLE audit_log (
    id          bigserial PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    actor       text NOT NULL,           -- the authenticated user, not the DB role
    action      text NOT NULL,
    entity      text NOT NULL,
    entity_id   text NOT NULL,
    before      jsonb,
    after       jsonb,
    request_id  text                     -- correlate with application traces
);

REVOKE UPDATE, DELETE ON audit_log FROM app_rw;    -- append-only, enforced
CREATE INDEX ix_audit_entity ON audit_log (entity, entity_id, occurred_at DESC);
CREATE INDEX ix_audit_actor  ON audit_log (actor, occurred_at DESC);

-- partition it by month, so retention is a DROP rather than a DELETE
```

| Requirement typically asked for                 | What satisfies it                                                   |
|-------------------------------------------------|---------------------------------------------------------------------|
| Who accessed this customer's record?            | Read auditing on sensitive tables — expensive, so scope it narrowly |
| Who changed this value, and what was it before? | An audit table with before/after, or temporal tables                |
| Prove the audit log has not been altered        | Append-only privileges, plus off-box shipping or hash chaining      |
| Retain for seven years                          | Partitioning plus archival to cold storage                          |
| Show that access is least-privilege             | Documented role grants, reviewed on a schedule                      |
| Erase a subject's data on request               | A tested erasure path that reaches every copy                       |

> **\[Note\]**
>
> **Record the actor, not the connection.** With connection pooling every statement comes from the same database role, so `current_user` tells you nothing useful. The application must pass the authenticated end user into the audit record — via a session variable set per transaction, or explicitly in the insert. Without that, your audit trail says only that the application did it.

<a id="be-d473"></a>

#### PII, retention and erasure

Holding personal data creates obligations that are enforced by regulators rather than by your test suite: know what you hold, hold it no longer than necessary, and be able to remove it.

1.  **Classify.** Tag every column that holds personal data, and record the classification in the schema itself so it cannot drift from the documentation.
2.  **Minimise.** The cheapest compliance measure is not collecting the field, and the second cheapest is not copying it into three other systems.
3.  **Set retention per class.** Different data has different lawful retention periods; encode them somewhere machine-readable.
4.  **Automate deletion.** A retention policy nobody executes is worse than none, because it documents a failure.
5.  **Build and test the erasure path** — including replicas, search indexes, caches, warehouses, backups and log files.
6.  **Audit the erasure itself**, which is permitted and usually required.

``` sql
-- classification as a first-class part of the schema
COMMENT ON COLUMN customers.email IS 'PII:contact; retention:account+3y';
COMMENT ON COLUMN customers.ssn   IS 'PII:sensitive; retention:legal-hold; encrypted';

-- so it can be inventoried automatically
SELECT c.table_name, c.column_name, d.description
FROM   information_schema.columns c
JOIN   pg_catalog.pg_description d
       ON d.objoid = (quote_ident(c.table_schema)||'.'||quote_ident(c.table_name))::regclass
      AND d.objsubid = c.ordinal_position
WHERE  d.description LIKE 'PII%';
```

``` sql
-- anonymisation rather than deletion: keeps referential integrity and analytics
-- valid, while removing the ability to identify the person
UPDATE customers SET
    name        = 'REDACTED',
    email       = 'redacted+' || id || '@invalid',
    phone       = NULL,
    address     = NULL,
    date_of_birth = date_trunc('year', date_of_birth),   -- coarsen rather than delete
    anonymised_at = now()
WHERE id = :subject_id;

-- orders, invoices and revenue figures remain intact and correct.
-- for data in an immutable log, the equivalent is CRYPTO-SHREDDING:
-- encrypt each subject's fields with a per-subject key, and destroy the key.
```

| Copy that people forget    | Why it matters                                                         |
|----------------------------|------------------------------------------------------------------------|
| Read replicas              | Erasure replicates, so this one is usually fine — verify it            |
| Backups                    | Cannot be edited. Document the retention window as the residual period |
| The data warehouse         | Frequently holds a full copy with its own retention                    |
| Search indexes             | Not transactional — needs its own deletion path                        |
| Caches                     | May hold the record for hours after erasure                            |
| Application and query logs | Personal data leaks into logs constantly                               |
| Exported reports and files | The hardest to track; restrict exports for this reason                 |

> **\[Note\]**
>
> **Write the copy inventory before you need it.** The list above, filled in for your own system, is the single most useful compliance artefact you can produce. Discovering the fourth copy during a regulator's deadline is not the moment to start looking.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a208"></a>

#### Supply chain security

Your application's attack surface includes every package you reference, every base image you build on, and every action in your pipeline. This is now a routine compliance requirement rather than an advanced topic.

``` text
  the chain, and where it is attacked

  developer machine   -> typosquatted package name, malicious postinstall
  package feed        -> dependency confusion: an internal name published publicly
  transitive deps     -> a compromised maintainer account three levels down
  base image          -> an unpatched CVE, or a tag silently repointed
  CI pipeline         -> a third-party action with write access to your registry
  registry            -> an unsigned image swapped for another
  cluster             -> no admission policy, so anything that is pulled runs
```

``` bash
# 1. lock what you resolve, and audit it
dotnet restore --use-lock-file          # commits packages.lock.json - reproducible restores
dotnet list package --vulnerable --include-transitive --format json
dotnet list package --deprecated
dotnet list package --outdated

# 2. pin your feeds and disallow silent fallback to nuget.org for internal names
```

``` text
<!-- nuget.config: dependency confusion defence -->
<configuration>
  <packageSources>
    <clear />
    <add key="internal" value="https://pkgs.internal/nuget/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="internal">
      <package pattern="Acme.*" />              <!-- our prefix NEVER resolves externally -->
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

``` bash
# 3. generate an SBOM, scan the image, and sign it
dotnet sbom-tool generate -b ./publish -bc . -pn Orders -pv 1.14.2 -ps Acme
trivy image --severity HIGH,CRITICAL --exit-code 1 registry.internal/orders:1.14.2
cosign sign --key awskms://... registry.internal/orders:1.14.2

# 4. and refuse to run anything unsigned
# Kyverno / Gatekeeper policy: verifyImages -> only images signed by our key
```

| Control                          | Prevents                                                | Effort                                      |
|----------------------------------|---------------------------------------------------------|---------------------------------------------|
| Lock files                       | A resolved version changing silently between builds     | Trivial                                     |
| `--vulnerable` gate in CI        | Shipping a known CVE                                    | Trivial — do this today                     |
| Package source mapping           | Dependency confusion                                    | Trivial, and high value                     |
| Pinned base image digests        | A tag being repointed under you                         | Low; needs a renovation bot to stay current |
| SBOM generation                  | Not knowing what you shipped when a CVE lands           | Low; increasingly required by customers     |
| Image signing + admission policy | Running an image you did not build                      | Medium                                      |
| Pinned CI actions by SHA         | A compromised third-party action                        | Low                                         |
| Internal package review          | A malicious or abandoned dependency entering the estate | Ongoing, organisational                     |

> **\[Note\]**
>
> **The two highest-value items.** A vulnerability gate in CI and package source mapping. Together they take under an hour, and they close the two ways a .NET shop is most likely to be compromised through its dependencies. Everything else on the list is worth doing and is less urgent.

###### Dependency confusion, and pinning your feeds

``` text
  DEPENDENCY CONFUSION - a live attack, not a theoretical one

  your nuget.config lists TWO feeds:
    https://pkgs.acme.internal/v3/index.json     (private)
    https://api.nuget.org/v3/index.json          (public)

  you reference:  Acme.Common  version 2.1.0

  NuGet queries BOTH. An attacker publishes Acme.Common 99.0.0 to the
  PUBLIC feed. Depending on resolution and version rules, the public
  package can win.

  and it EXECUTES AT BUILD TIME, on your machine and on your CI agent,
  with whatever credentials that agent holds.

  the fix is package source mapping: bind each PREFIX to exactly ONE feed.
```

``` text
<!-- nuget.config - this file is the control -->
<configuration>
  <packageSources>
    <clear />                                    <!-- ignore inherited sources -->
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
    <add key="acme"  value="https://pkgs.acme.internal/v3/index.json" />
  </packageSources>

  <packageSourceMapping>
    <packageSource key="acme">
      <package pattern="Acme.*" />               <!-- ONLY from the private feed -->
    </packageSource>
    <packageSource key="nuget">
      <package pattern="*" />                    <!-- everything else -->
    </packageSource>
  </packageSourceMapping>
</configuration>

<!-- and reserve your prefix on nuget.org, so nobody else can publish
     under Acme.* at all. Prefix reservation is free and takes minutes. -->
```

| Control                                  | Protects against                                |
|------------------------------------------|-------------------------------------------------|
| Package source mapping                   | Dependency confusion                            |
| Prefix reservation on nuget.org          | Anyone publishing under your namespace          |
| `packages.lock.json` + `--locked-mode`   | A transitive dependency changing under you      |
| `dotnet list package --vulnerable` in CI | Known CVEs shipping                             |
| NuGet audit (`NuGetAuditMode=all`)       | The same, including transitive                  |
| SBOM generation                          | Not knowing what you shipped when a CVE lands   |
| Signed packages and artifact signing     | Tampering between build and deploy              |
| Restricted egress on build agents        | A malicious install script exfiltrating secrets |
| Review of new dependencies               | Typosquats and abandoned packages               |

> **\[Trap\]**
>
> **Common trap.** The CI/CD pipeline is a high-value target and is frequently the weakest link: it can deploy to production, read every secret, and it runs arbitrary code from your repository. Never expose production secrets to a build triggered by a pull request from a fork — that combination is a complete compromise, and it has happened to real companies. Use environment protection rules, OIDC federation instead of stored cloud keys, and require review on the pipeline definitions themselves.

### Platform Security

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Platform Security</p>

<p class="pd">Security as a property of the deployed system rather than the code: identity, secrets, patching and the posture that limits what a compromise can reach.</p>

<a id="be-g713"></a>

#### Patching, supply chain and posture

| Layer                       | Patching mechanism                     | Cadence                                                 |
|-----------------------------|----------------------------------------|---------------------------------------------------------|
| Base image                  | Rebuild from an updated base           | Weekly, automated                                       |
| OS packages (VMs)           | Unattended upgrades, or image rebuild  | Weekly for security                                     |
| Application dependencies    | Dependabot/Renovate + tests            | Continuous                                              |
| Runtime (.NET, Node)        | Patch version in the base image        | With the base image                                     |
| Database engine             | Managed maintenance window, or planned | Quarterly at minimum                                    |
| Infrastructure (Kubernetes) | Version upgrades                       | Follow the support policy — falling behind is a project |

``` yaml
# a rebuild-on-schedule pipeline matters as much as a rebuild-on-commit one.
# an image built three months ago and never redeployed carries three months
# of unpatched CVEs, even though the application code never changed.
schedule:
  - cron: '0 3 * * 1'      # weekly rebuild from the latest patched base

# scan, and fail on what matters - not on everything, or it gets ignored
trivy image --severity HIGH,CRITICAL --exit-code 1 myapp:$SHA
trivy image --ignore-unfixed ...    # do not block on what has no fix yet
```

| Supply chain control                | Protects against                                |
|-------------------------------------|-------------------------------------------------|
| Lockfiles, committed                | A dependency changing under you                 |
| Pin base images by digest           | A rebuilt tag introducing something new         |
| Private registry mirror             | Upstream deletion or compromise                 |
| SBOM generation                     | Not knowing what you shipped when a CVE lands   |
| Artifact signing (Sigstore, cosign) | A tampered artifact being deployed              |
| Provenance attestation              | Proving an artifact came from your pipeline     |
| Restricted egress from build agents | A malicious install script exfiltrating secrets |
| Review of new dependencies          | Typosquats and abandoned packages               |

``` text
  the CI/CD pipeline is a high-value target and is often the weakest link

  it can:  deploy to production, read every secret, sign artifacts,
           and it runs arbitrary code from your repository

  so:  scope its credentials narrowly, per environment
       require review for changes to pipeline definitions themselves
       do not expose production secrets to pull-request builds
         (a PR from a fork running with production credentials is a
          complete compromise, and it has happened to real companies)
       use OIDC federation rather than long-lived cloud keys
       log and alert on pipeline permission changes
```

> **\[Note\]**
>
> **The boring controls do the most.** Multi-factor authentication, least privilege, current patches, no standing production access, network segmentation, encrypted and immutable backups, and secrets that expire. None of it is interesting, all of it is well understood, and it prevents the overwhelming majority of real incidents. The exotic threat is rarely what gets you; an unpatched dependency and an over-privileged role usually is.

<a id="be-g714"></a>

#### A platform checklist, and where to go next

A single pass over a system, in the order things tend to break.

| Area              | Ask                                                                                                                                  |
|-------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| **Networking**    | Is anything in a public subnet that need not be? Is egress restricted? Are security groups referencing groups rather than IP ranges? |
| **TLS**           | Automated renewal? Monitored from outside? Full chain served? Internal traffic encrypted?                                            |
| **Servers**       | Running as non-root? Resource limits set? Automatic restarts bounded? Logs shipped off the box?                                      |
| **Access**        | Any standing production access? Any shared accounts? Any long-lived cloud keys?                                                      |
| **Compute**       | Memory limits set? CPU limits omitted or deliberate? Graceful shutdown implemented and tested?                                       |
| **Storage**       | Database on block storage? Free space monitored with headroom? Backups in a separate account, immutable?                             |
| **Database**      | Working set in memory? A pooler in front? THP disabled? Failover tested?                                                             |
| **Deployment**    | Build once? Config external? Migrations expand-and-contract? Rollback tested?                                                        |
| **Observability** | Trace ids propagated? Alerts on symptoms? Runbooks reachable during an outage?                                                       |
| **Recovery**      | Restore drilled and timed? RTO and RPO written down and agreed?                                                                      |

##### Where to go deeper

- **Build one of everything yourself, once.** A VM, nginx in front of an app, a certificate from Let's Encrypt, a systemd unit, a database on a separate disk. The abstractions make far more sense once you have assembled the thing they abstract.
- **Break things deliberately in a scratch environment.** Fill a disk, expire a certificate, kill a primary, block a security group, exhaust file descriptors. Each turns a paragraph here into recognition.
- **Read one production incident report a week.** Cloudflare, GitLab, AWS and Cloudflare publish excellent ones. The failure modes repeat, and recognising them is most of operational skill.
- **Learn `tcpdump` and `EXPLAIN`.** Between them they answer most “why is this slow?” questions that dashboards cannot.
- **Read the standard texts** — Google's *Site Reliability Engineering* (free online), Kleppmann's *Designing Data-Intensive Applications*, Nygard's *Release It!* for failure patterns.

> **\[Note\]**
>
> **The through-line.** Everything here is one idea repeated: *the abstraction your code sits on is real machinery with real limits — packets, sockets, file descriptors, pages, disks, processes.* Most of the time you can ignore it, which is the point of abstraction. When something is slow, broken or insecure, the answer is almost always at a layer below the one you were looking at — and knowing which layer to drop to is the whole skill.
