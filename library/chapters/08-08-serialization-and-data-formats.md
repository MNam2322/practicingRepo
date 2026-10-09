---
id: "8-8"
number: "8.8"
title: "Serialization and data formats"
part: 8
part_title: "Building a backend"
words: 4487
minutes: 20
---

# 8.8 Serialization and data formats

> **Quick look.** Turning objects into bytes and back: JSON, Protocol Buffers, CSV and documents.
>
> - System.Text.Json is strict where Newtonsoft was lenient; most migration pain comes from that.
> - Reuse JsonSerializerOptions; a new instance per call is a common performance bug.
> - Source-generated serialization is required for AOT and faster everywhere.
> - Serializing EF entities with cycles breaks: return DTOs.
> - Protobuf compatibility depends on never reusing field numbers; schema registries enforce event contracts.
> - CSV is harder than Split(','): use a real parser and guard against formula injection.
> - Stream large exports and documents instead of building them in memory.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Serializer options created per request

A profiler shows 15% of CPU in `JsonSerializerOptions` metadata building.

*Create once, reuse*

```csharp
// Before: JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);   // cached metadata
var payload = JsonSerializer.Serialize(obj, Json);
```

> **Lesson:** JsonSerializerOptions caches type metadata. A new instance per call throws that cache away.

#### Example 2: A CSV export that ran formulas in Excel

A customer's name `=HYPERLINK("http://evil","Click")` exported to CSV becomes a live link in finance's spreadsheet.

*Neutralize formula characters*

```csharp
static string SafeCell(string? s)
{
    if (string.IsNullOrEmpty(s)) return "";
    if (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;   // treat as text
    return s;
}
```

> **Lesson:** Exports are an output boundary too. Encode for the destination, including spreadsheets.

### Serialization (System.Text.Json)

<p class="src">From <em>.NET Backend Deep Dive</em> · Serialization (System.Text.Json)</p>

<a id="dn-114"></a>

#### Defaults that surprise people

```csharp
var opts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,  // ASP.NET Core default; direct
                                                        // JsonSerializer.Serialize is NOT
    PropertyNameCaseInsensitive = true,                 // slower; explicit is better
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReferenceHandler = ReferenceHandler.IgnoreCycles    // EF navigation loops
};
```

- **`System.Text.Json` is strict where Newtonsoft was lenient.** Trailing commas, comments, single quotes and unquoted names all throw by default. Most migration pain comes from this.
- **Fields are not serialized** unless you set `IncludeFields = true`. Properties only.
- **Deserialization requires a settable or `init` property and a usable constructor.** Records work; classes with only getters silently produce defaults.
- **`Dictionary<TKey,_>` keys must be strings or simple types** — a complex key needs a converter.

<a id="dn-115"></a>

#### Cycles and EF entities

Serializing an EF entity with bidirectional navigations (`Order.Customer.Orders`) throws or loops. `ReferenceHandler.IgnoreCycles` masks the symptom; the actual fix is to **return DTOs**, which also solves over-posting, field leakage and API-versus-schema coupling in one move.

<a id="dn-116"></a>

#### Source generation — required for AOT, faster everywhere

```csharp
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(List<OrderDto>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext { }

// Registration
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

// Direct use
JsonSerializer.Serialize(dto, AppJsonContext.Default.OrderDto);
```

Reflection-based serialization doesn't survive trimming or Native AOT, and the generated path is meaningfully faster with no startup reflection cost.

<a id="dn-117"></a>

#### Custom converters

```csharp
// Money as a string to avoid float/precision issues on the wire
public class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        => decimal.Parse(r.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter w, decimal v, JsonSerializerOptions o)
        => w.WriteStringValue(v.ToString(CultureInfo.InvariantCulture));
}
```

**Reuse `JsonSerializerOptions`.** Constructing a new instance per call defeats the internal metadata cache and is dramatically slower — a genuinely common performance bug.

<a id="dn-118"></a>

#### Dates, enums, and interop

```csharp
// Serialize enums as names, not integers - integers break when someone reorders the enum
opts.Converters.Add(new JsonStringEnumConverter());

// Always UTC on the wire, ISO 8601 with offset
// Prefer DateTimeOffset over DateTime: DateTime silently loses the offset
```

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a36"></a>

#### System.Text.Json in practice

`System.Text.Json` is the default: fast, UTF-8 native, and strict. Its strictness is the main source of migration pain from Newtonsoft — it is case-sensitive by default in some paths, will not silently coerce types, and has no polymorphic handling unless you ask for it.

``` csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    Converters = { new JsonStringEnumConverter() }
};

// polymorphism, declared explicitly
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CardPayment),     "card")]
[JsonDerivedType(typeof(TransferPayment), "transfer")]
public abstract class Payment { }

// source generation: no reflection, AOT-safe, measurably faster
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OrderResponse))]
internal partial class ApiJsonContext : JsonSerializerContext { }
```

| Situation                  | Watch for                                                                |
|----------------------------|--------------------------------------------------------------------------|
| Cycles in the object graph | Throws by default; use `ReferenceHandler.IgnoreCycles` or serialize DTOs |
| `decimal` money            | Correct by default; never use `double` for currency                      |
| Very large payloads        | Stream with `DeserializeAsync`; never `ReadAsStringAsync` first          |
| Unknown fields             | Ignored by default — this is what makes tolerant readers work            |
| `Dictionary<enum, T>`      | Needs a converter; a common runtime surprise                             |

> **\[Trap\]**
>
> **Common trap.** Never deserialize untrusted input into polymorphic types without an explicit allow-list of derived types. Unrestricted type resolution is a remote code execution vector — the reason `BinaryFormatter` was removed from .NET entirely.

###### Source generation, polymorphism and the defaults that surprise

| Default                      | Behaviour                                    | Change with                                             |
|------------------------------|----------------------------------------------|---------------------------------------------------------|
| Property naming              | **Case-insensitive on read**, exact on write | `PropertyNamingPolicy = CamelCase`                      |
| Fields                       | **Ignored** — only properties are serialised | `IncludeFields = true`                                  |
| `null` values                | Written                                      | `DefaultIgnoreCondition`                                |
| Non-ASCII                    | Escaped as `\uXXXX`                          | `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` |
| Comments and trailing commas | Rejected                                     | `ReadCommentHandling`, `AllowTrailingCommas`            |
| Numbers as strings           | Rejected                                     | `NumberHandling = AllowReadingFromString`               |
| Cycles                       | Throws                                       | `ReferenceHandler.Preserve` — or fix the model          |
| Read-only properties         | Not populated on deserialise                 | A constructor parameter, or a setter                    |

``` csharp
// configure ONCE, as a static, and reuse. Creating JsonSerializerOptions
// per call defeats the internal metadata cache and is measurably slow.
public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
};
// JsonSerializerDefaults.Web already sets camelCase + case-insensitive read,
// which is what ASP.NET Core uses.
```

``` csharp
// polymorphic serialisation, without a custom converter (.NET 7+)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CardPayment), "card")]
[JsonDerivedType(typeof(BankTransfer), "transfer")]
public abstract record Payment(decimal Amount);

// {"$type":"card","amount":99.50,"last4":"4242"}
// note: the discriminator must come FIRST in the JSON when deserialising,
// because the reader is forward-only. Most producers do this; some do not.

// custom converter, for a domain type that should be a scalar in JSON
public sealed class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        => new(r.GetDecimal());
    public override void Write(Utf8JsonWriter w, Money v, JsonSerializerOptions o)
        => w.WriteNumberValue(v.Amount);
}
```

``` csharp
// source-generated contracts: faster startup, less allocation, AOT-safe
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(List<Order>))]     // every ROOT type you serialise
internal partial class AppJson : JsonSerializerContext { }

var json = JsonSerializer.Serialize(order, AppJson.Default.Order);
var back = JsonSerializer.Deserialize(json, AppJson.Default.Order);

// wire it into ASP.NET Core
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));
```

> **\[Trap\]**
>
> **Common trap.** Deserialising untrusted JSON without limits is a denial-of-service vector: deeply nested arrays exhaust the stack, and very large payloads exhaust memory. Set `MaxDepth` (the default of 64 is usually fine, but verify it), enforce a request body size limit at the server, and never deserialise into a type with a `Dictionary<string, object>` that will happily accept anything a caller sends.

### Beyond the relational store

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Beyond the relational store</p>

<a id="be-m208"></a>

#### System.Text.Json: shape and cost

``` csharp
var opts = new JsonSerializerOptions {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = JsonCommentHandling.Skip,
};
// Cache this instance. Constructing JsonSerializerOptions per call rebuilds the
// entire metadata cache and is one of the most common serialization perf bugs.
```

Work with the narrowest input you have. Every conversion up the chain costs an allocation and a copy:

``` text
JsonSerializer.Deserialize<T>(stream);          // best - UTF-8 all the way
JsonSerializer.Deserialize<T>(utf8Bytes);       // good
JsonSerializer.Deserialize<T>(jsonString);      // transcodes UTF-16 -> UTF-8 first
```

| Concern         | Handling                                                      |
|-----------------|---------------------------------------------------------------|
| Property naming | `PropertyNamingPolicy`, or `[JsonPropertyName]`               |
| Ignoring        | `[JsonIgnore]`, `DefaultIgnoreCondition`                      |
| Custom types    | `JsonConverter<T>`                                            |
| Polymorphism    | `[JsonDerivedType(typeof(X), "x")]` (.NET 7+)                 |
| Cycles          | `ReferenceHandler.Preserve` — but prefer a DTO without cycles |
| Unknown fields  | `[JsonExtensionData]` to round-trip them                      |
| Big documents   | `Utf8JsonReader` for streaming, `JsonNode` for mutation       |

``` csharp
// Source-generated serialization: no reflection, AOT-safe, faster startup
[JsonSerializable(typeof(OrderDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext { }

JsonSerializer.Serialize(order, AppJsonContext.Default.OrderDto);
```

> **\[Trap\]**
>
> **Differences from Newtonsoft that bite on migration:** `System.Text.Json` is case-sensitive by default (set `PropertyNameCaseInsensitive`), does not serialize fields unless told to, is strict about numbers-as-strings, does not handle `TimeSpan` or `DateTime` formats as leniently, and requires an explicit converter for dictionary keys that are not strings. Migrating a public API contract needs a diff of actual payloads, not a code review.

Goes deeper: [Choosing a wire format](#be-m92)

### Crossing boundaries

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Crossing boundaries</p>

<a id="be-m92"></a>

#### Choosing a wire format

| Format           | Schema                          | Size            | Human-readable | Best for                                                                |
|------------------|---------------------------------|-----------------|----------------|-------------------------------------------------------------------------|
| JSON             | none (or JSON Schema)           | baseline        | yes            | public APIs, config, debuggability                                      |
| MessagePack      | none, or attributes             | ~30–50% of JSON | no             | internal RPC, cache values                                              |
| Protocol Buffers | `.proto`, required              | ~20–40%         | no             | gRPC, cross-language services                                           |
| Avro             | schema travels or is registered | ~20–40%         | no             | event streams, Kafka                                                    |
| Parquet          | columnar schema                 | very small      | no             | analytical files, data lakes                                            |
| CSV              | none                            | large           | yes            | human interchange only ([Tabular interchange: CSV and Excel](#be-m219)) |

**Size is rarely the deciding factor.** Compressed JSON is often close to uncompressed protobuf ([Compression: when and what](01-06-storage-and-files.md#be-m91)), and for a service doing database work the serialization cost is noise. The real questions are different:

- **Who else reads it?** A public API argues strongly for JSON, whatever its cost, because every client already speaks it and you can debug it with `curl`.
- **Is there a contract?** A schema-first format makes incompatible changes a build failure instead of a production incident ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223)).
- **How long does it live?** A request payload lives for milliseconds; an event in a log lives for years and will be read by code not yet written. The latter needs a real schema and a compatibility policy.

> **\[Tip\]**
>
> **A reasonable default split:** JSON at the public edge, something binary and schema‑bearing between your own services and in event streams, and Parquet for anything analytical. Optimising the edge format is usually the least valuable of the three — that is where debuggability is worth the most and where the payload is smallest.

<a id="be-m93"></a>

#### Protocol Buffers and gRPC

``` csharp
syntax = "proto3";
message Order {
  int64  id          = 1;    // the FIELD NUMBER is the contract, not the name
  string customer    = 2;
  int64  total_minor = 3;    // money as minor units - no decimal type in protobuf
  repeated OrderLine lines = 4;
  optional string note = 5;  // proto3 'optional' restores presence tracking
}
```

Encoding is compact because the wire carries *field number + wire type*, then the value — no names, and integers use varint encoding so small numbers cost one byte. That design dictates the compatibility rules:

| Change                            | Safe?                                |
|-----------------------------------|--------------------------------------|
| add a new field with a new number | yes — old readers skip it            |
| rename a field, same number       | yes — names are not on the wire      |
| **reuse a retired field number**  | never — old data decodes as garbage  |
| change a field's type             | no, except a few compatible pairs    |
| remove a field                    | reserve the number: `reserved 5;`    |
| add an enum value                 | readers must tolerate unknown values |

``` csharp
// .NET: Grpc.AspNetCore generates client and server from the .proto at build.
var client = new Orders.OrdersClient(channel);
var reply = await client.GetOrderAsync(new GetOrderRequest { Id = 42 },
                                       deadline: DateTime.UtcNow.AddSeconds(5),
                                       cancellationToken: ct);
// Deadlines propagate across hops - a genuine advantage over ad-hoc HTTP,
// where each service invents its own timeout (§137).
```

> **\[Trap\]**
>
> **proto3 has no null for scalars.** An unset `int64` and a set-to-zero `int64` are indistinguishable unless you mark the field `optional` or use a wrapper type. Modelling "quantity not supplied" as `0` is a real data-loss bug in inventory and pricing code — the same three-valued-logic problem as SQL nulls, in a different costume ([NULL handling in SQL](07-02-the-relational-model-and-sql.md#be-m187)). Also: protobuf has no decimal type, so money must be minor units or a string ([Money arithmetic and rounding](01-02-numbers-inside-the-machine.md#be-m69)).

First principles: [HTTP/1.1, HTTP/2, and HTTP/3](06-06-http.md#be-a159)

<a id="be-m94"></a>

#### Schema registries and compatibility

Once messages outlive the process that wrote them, "which version is this?" becomes a runtime question. A schema registry answers it: producers register a schema and get an id, the message carries the id, and consumers fetch the schema to decode.

``` text
// Avro on Kafka: the message is [magic byte][4-byte schema id][payload].
// The consumer resolves the WRITER's schema from the registry and reconciles
// it against its own READER schema - so a consumer compiled last year can
// still read a message written today, as long as the change was compatible.
```

| Compatibility mode | Guarantees                | You may                            |
|--------------------|---------------------------|------------------------------------|
| **Backward**       | new readers read old data | delete fields; add optional fields |
| **Forward**        | old readers read new data | add fields; delete optional fields |
| **Full**           | both                      | add or delete optional fields only |
| None               | nothing                   | anything, and you will regret it   |

**Which mode you need depends on deployment order.** If consumers upgrade first, you need backward compatibility. If producers upgrade first, forward. During a rolling deploy both versions run at once, so in practice you want **full** — which means every new field is optional with a default, and no field is ever removed until every consumer has stopped reading it.

``` text
// Enforce it in CI, before it reaches a broker:
//   1. fetch the currently registered schema for the subject
//   2. check the new schema against it in the required mode
//   3. fail the build on incompatibility
// This turns a 3am production decode failure into a red pull request.
```

> **\[Tip\]**
>
> **You do not need a registry to get the benefit.** A version number in the envelope plus a compatibility test in CI covers most of it ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223)). Registries earn their operational cost when many independent teams produce to shared topics — the value is the enforced contract and the audit trail, not the storage.

> **\[Trap\]**
>
> **The registry becomes a hard dependency.** If consumers cannot reach it, they cannot decode. Cache schemas aggressively and locally, treat a registry outage as a degraded-but-running condition, and never make schema resolution a synchronous step on a hot path ([Resilience: retries, breakers, and timeouts](12-05-reliability.md#be-m263)).

Goes further: [Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249)

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a151"></a>

#### Schema registries and contract governance

Once more than one team publishes events, “do not break the contract” needs mechanical enforcement. A schema registry stores every version of every message schema and refuses registrations that violate a declared compatibility rule.

| Compatibility mode | A new schema may                                | Meaning                                                                       |
|--------------------|-------------------------------------------------|-------------------------------------------------------------------------------|
| `BACKWARD`         | Delete optional fields, add optional fields     | **New consumers** can read **old** data — upgrade consumers first             |
| `FORWARD`          | Add fields, delete optional fields              | **Old consumers** can read **new** data — upgrade producers first             |
| `FULL`             | Only add or remove optional fields              | Both directions; the safest and most restrictive                              |
| `*_TRANSITIVE`     | Same, but checked against *every* prior version | What you want — otherwise compatibility silently drifts over several releases |
| `NONE`             | Anything                                        | A registry providing documentation and no protection                          |

``` csharp
// producer: the schema is registered and the message carries its schema id
var config = new SchemaRegistryConfig { Url = "http://schema-registry:8081" };
using var registry = new CachedSchemaRegistryClient(config);

using var producer = new ProducerBuilder<string, OrderPlaced>(producerConfig)
    .SetValueSerializer(new JsonSerializer<OrderPlaced>(registry,
        new JsonSerializerConfig { AutoRegisterSchemas = false,      // CI registers, not runtime
                                   UseLatestVersion = true }))
    .Build();

await producer.ProduceAsync("orders.placed",
    new Message<string, OrderPlaced> { Key = orderId.ToString(), Value = evt });
```

``` bash
# CI gate: registering the schema IS the compatibility check
- name: Check event schema compatibility
  run: |
    curl -sf -X POST \
      -H "Content-Type: application/vnd.schemaregistry.v1+json" \
      --data @schemas/orders.placed.json \
      http://schema-registry:8081/compatibility/subjects/orders.placed-value/versions/latest \
      | jq -e '.is_compatible == true'
    # non-zero exit -> the pull request fails, before anything is deployed
```

###### Governance without a registry, and what to do for HTTP and gRPC

A registry is a Kafka-ecosystem tool. The underlying discipline applies regardless of transport, and there is an equivalent mechanism for each.

| Transport              | Contract artifact                                   | CI gate                                            |
|------------------------|-----------------------------------------------------|----------------------------------------------------|
| Kafka                  | Avro / JSON Schema / Protobuf in a registry         | Compatibility check on register                    |
| gRPC                   | `.proto` files in a shared repository               | `buf breaking` against the main branch             |
| HTTP / REST            | OpenAPI document, published as a build artifact     | `oasdiff` or Spectral, failing on breaking changes |
| RabbitMQ / Service Bus | The message contract assembly, or JSON Schema files | Consumer-driven contract tests (PactNet)           |

``` text
# OpenAPI: fail the build on a breaking API change
- run: dotnet run --project src/Orders.Api -- --generate-openapi > openapi.new.json
- run: oasdiff breaking openapi.baseline.json openapi.new.json --fail-on ERR
```

1.  **Contracts live in a place both sides can see** — a shared repo, a package, or a registry. Not inside the producer's source tree only.
2.  **Compatibility is checked by a machine** on every pull request. Human review does not reliably catch a narrowed field type.
3.  **Every consumer is a tolerant reader:** ignore unknown fields, never fail on additions. This is what makes additive change safe.
4.  **Deprecate before removing.** Mark the field, measure whether anyone still reads it, remove it a release later.
5.  **Own the schema where the data originates.** A contract owned by a consumer becomes a contract nobody can change.

> **\[Trap\]**
>
> **Common trap.** Sharing a `Contracts.dll` between producer and consumer is convenient and quietly recreates deployment coupling: a version bump forces every consumer to rebuild, and the compiler becomes the compatibility check — which means there is no compatibility check at all, only a break. If you do share a package, keep it tiny, additive-only, and independently versioned.

### Modeling patterns that recur

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Modeling patterns that recur</p>

<a id="be-m219"></a>

#### Tabular interchange: CSV and Excel

CSV looks trivial and is not. RFC 4180 is a loose guideline that almost nothing follows exactly, so a hand-rolled `Split(',')` parser will fail on real files.

| Hazard             | Example                                                |
|--------------------|--------------------------------------------------------|
| Embedded delimiter | `"Nguyen, Nam",42`                                     |
| Embedded quote     | `"He said ""hi""",1`                                   |
| Embedded newline   | a quoted field spanning two physical lines             |
| BOM                | the first header name silently becomes `\uFEFFId`      |
| Locale decimals    | `1.234,56` in a European export                        |
| Excel semicolons   | Excel writes `;` when the locale list separator is `;` |
| Leading zeros      | Excel turns `00123` into `123` on open                 |
| Long digit strings | a 16-digit id becomes `1.23457E+15`                    |

``` csharp
// Use a real parser. CsvHelper handles all of the above and streams.
using var reader = new StreamReader(stream, new UTF8Encoding(false));
using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) {
    DetectDelimiter = true,
    TrimOptions = TrimOptions.Trim,
    MissingFieldFound = null,          // decide explicitly rather than throwing
    HeaderValidated = null
});
await foreach (var row in csv.GetRecordsAsync<ImportRow>(ct))
    yield return row;                   // streaming, bounded memory (§299)
```

> **\[Trap\]** **CSV formula injection.** A field beginning with `=`, `+`, `-`, or `@` is interpreted as a formula when the file is opened in a spreadsheet — and formulas can invoke external commands. If you export user-supplied text, prefix such fields with a single quote or a tab. This is a real, exploited vulnerability class, and it is invisible in your own application ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)).
>
> ``` csharp
> static string SafeCell(string v) =>
>     v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + v : v;
> ```

> **\[Tip\]**
>
> **Prefer a real format when you control both ends.** JSON Lines gives you typed values and streaming with none of the quoting ambiguity; Parquet gives columnar compression and typed schemas for analytical handoff. Reserve CSV for interoperability with people and tools that demand it — and when writing `.xlsx`, use a proper library (ClosedXML, EPPlus) rather than emitting CSV and hoping Excel guesses your types correctly, because it will not.

Worked example: [Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a154"></a>

#### Generating documents: PDF, Excel and CSV

Producing an invoice, an export or a report is a large share of real backend work and gets almost no coverage in learning material. The library choice matters less than the three decisions around it: templating, streaming, and what happens in a container.

| Output        | Library                     | Notes                                                                                   |
|---------------|-----------------------------|-----------------------------------------------------------------------------------------|
| PDF           | QuestPDF                    | Fluent C# layout API, MIT for small companies, actively developed — the current default |
| PDF           | iText / IronPDF             | Commercial; iText is AGPL unless licensed — check before shipping                       |
| PDF from HTML | PuppeteerSharp, Playwright  | Reuse web templates; needs a headless browser in the image (~300 MB)                    |
| Excel         | ClosedXML                   | Easy, in-memory, MIT; fine up to tens of thousands of rows                              |
| Excel         | EPPlus                      | Polyform licence since v5 — commercial for commercial use                               |
| Excel (large) | OpenXmlWriter, or MiniExcel | Streams rows; required past ~100k rows                                                  |
| CSV           | CsvHelper                   | The standard. Handles quoting, culture and type conversion correctly                    |

``` csharp
// QuestPDF: layout is code, so it is reviewable, testable and refactorable
public sealed class InvoiceDocument(Invoice invoice) : IDocument
{
    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Margin(40);
        page.Size(PageSizes.A4);
        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato"));

        page.Header().Row(row =>
        {
            row.RelativeItem().Text($"Invoice {invoice.Number}").FontSize(18).SemiBold();
            row.ConstantItem(120).AlignRight().Text(invoice.IssuedAt.ToString("d MMMM yyyy"));
        });

        page.Content().PaddingVertical(20).Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(4); c.ConstantColumn(50);
                                           c.ConstantColumn(70); c.ConstantColumn(80); });
            table.Header(h =>
            {
                h.Cell().Text("Description").SemiBold();
                h.Cell().AlignRight().Text("Qty").SemiBold();
                h.Cell().AlignRight().Text("Unit").SemiBold();
                h.Cell().AlignRight().Text("Total").SemiBold();
            });
            foreach (var line in invoice.Lines)
            {
                table.Cell().Text(line.Description);
                table.Cell().AlignRight().Text(line.Quantity.ToString());
                table.Cell().AlignRight().Text(line.UnitPrice.ToString());
                table.Cell().AlignRight().Text(line.Total.ToString());
            }
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
        });
    });
}

// render straight to the response, no intermediate byte[]
app.MapGet("/invoices/{id}/pdf", async (Guid id, IInvoices q, HttpContext ctx, CancellationToken ct) =>
{
    var invoice = await q.GetAsync(id, ct) ?? throw new NotFoundException("Invoice", id);
    ctx.Response.ContentType = "application/pdf";
    ctx.Response.Headers.ContentDisposition = $"attachment; filename="{invoice.Number}.pdf"";
    new InvoiceDocument(invoice).GeneratePdf(ctx.Response.Body);
});
```

###### Large exports: streaming, and why in-memory fails quietly

``` csharp
// CSV export of 2 million rows, streamed end to end - flat memory
app.MapGet("/exports/orders.csv", async (AppDb db, DateOnly from, HttpContext ctx,
                                         CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/csv; charset=utf-8";
    ctx.Response.Headers.ContentDisposition = "attachment; filename="orders.csv"";

    await using var writer = new StreamWriter(ctx.Response.Body, Encoding.UTF8);
    await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

    csv.WriteHeader<OrderExportRow>();
    await csv.NextRecordAsync();

    var rows = db.Orders.AsNoTracking()
        .Where(o => o.PlacedAt >= from.ToDateTime(TimeOnly.MinValue))
        .OrderBy(o => o.PlacedAt)
        .Select(o => new OrderExportRow(o.Id, o.PlacedAt, o.Total, o.Status.ToString()))
        .AsAsyncEnumerable();

    var n = 0;
    await foreach (var row in rows.WithCancellation(ct))
    {
        csv.WriteRecord(row);
        await csv.NextRecordAsync();
        if (++n % 5000 == 0) await ctx.Response.Body.FlushAsync(ct);   // keep the client fed
    }
});
```

| Row count                           | Approach                                                                  | Why                                                     |
|-------------------------------------|---------------------------------------------------------------------------|---------------------------------------------------------|
| Under ~10,000                       | Build in memory, return the bytes                                         | Simple; a few megabytes is fine                         |
| 10,000 – ~1 million                 | Stream directly to the response                                           | Flat memory; time to first byte is milliseconds         |
| Over ~1 million, or slow to produce | Async request-reply: a job writes to blob storage, the client gets a link | Survives client disconnects, proxy timeouts and retries |

> **\[Trap\]**
>
> **Common trap.** A 40-minute export streamed over an open HTTP connection will be cut by a load balancer idle timeout, a gateway limit, or a laptop closing its lid — and there is no way to resume, because you cannot change the status code after the first byte. Past a few minutes of generation time, the job-plus-link pattern is not a nicety; it is the only design that works.

###### Containers, fonts, culture and the failures you will hit first

``` csharp
# 1. NO FONTS. The classic first failure: PDF renders with blank or boxed text,
#    because chiseled and alpine images ship no font files at all.
RUN apt-get update && apt-get install -y --no-install-recommends \
    fontconfig fonts-liberation && rm -rf /var/lib/apt/lists/*
# or bundle the exact fonts and register them, which is more reproducible:
#   FontManager.RegisterFont(File.OpenRead("fonts/Lato-Regular.ttf"));

# 2. NO ICU. Number and date formatting throws, or silently formats invariantly.
#    Use a -extra base image, or accept InvariantGlobalization.

# 3. NO TZDATA. TimeZoneInfo.FindSystemTimeZoneById throws.
#    Needed if the document shows local times.

# 4. Headless-browser PDF needs a browser AND its dependencies (~300 MB).
#    Prefer a native library unless you genuinely need HTML/CSS fidelity.
```

- **Format for the reader, not the machine.** Documents are the one place `CurrentCulture` is correct — a Dutch customer expects `1.234,56`. Set the culture explicitly per document from the recipient's locale rather than relying on the server's.
- **Generation is CPU-bound.** A PDF render can take hundreds of milliseconds and does not yield. Ten concurrent renders will saturate a small container and starve the request pipeline — move bulk generation to a worker and bound its concurrency.
- **Snapshot the data, not a reference.** A regenerated invoice must be byte-identical years later. Store the rendered document, or store an immutable snapshot of the inputs. Re-rendering from live tables produces a different document and an audit finding.
- **Test the output, not just the code.** Snapshot-test the extracted text or a page hash. Layout regressions are invisible in unit tests and extremely visible to customers.
- **Excel is not CSV.** A `.csv` served as Excel gives users mangled dates and dropped leading zeros; a real `.xlsx` preserves types, and formulas and formatting survive.
