---
id: "8-5"
number: "8.5"
title: "Validation and errors at the boundary"
part: 8
part_title: "Building a backend"
words: 5594
minutes: 24
---

# 8.5 Validation and errors at the boundary

> **Quick look.** Check everything that crosses a trust boundary once, on arrival, and fail with one clear error contract.
>
> - Requests, messages, files and partner responses are all untrusted input.
> - Check shape at the edge, business rules in the domain against current data, invariants inside the type.
> - Parse input into types that can only hold valid values (value objects): allowlists, canonicalize first, then check, with size limits.
> - Report every field error at once.
> - Bind requests to dedicated DTOs, never to stored entities, to prevent over-posting.
> - One machine-readable error format with stable codes, no internals, and a correlation id that also appears in the logs.


### The big picture

Every backend has edges where data arrives from outside: request bodies, query strings, headers, uploaded files, messages from queues, responses from partner systems. None of it can be trusted. It may be mistyped, out of date, deliberately malicious, or simply shaped differently from what the documentation promised. A backend's reliability depends on checking that data the moment it arrives, and turning it into values the rest of the code can rely on.

This chapter covers that edge in both directions. Inbound, it explains what to check and where, why the best approach is to parse input into types that can only hold valid values, and how binding requests straight onto database entities lets attackers set fields they shouldn't. Outbound, it covers reporting failures as a consistent, machine-readable error contract that tells clients what went wrong without revealing internals. It also covers correlation ids, which let one failure be traced across every system it touched.

### Before you start

**Builds on:** 7.4 Transactions and isolation, 4.4 Immutability and pure functions, 2.3 Types, 1.3 Text is numbers too, 8.2 API design, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Availability** (10.1): A system works when legitimate users need it.
- **Authentication** (10.2): Establishing who a caller is.
- **Trace context** (11.6): The trace id and parent span id passed between services, in the W3C traceparent header.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **HTML** (9.1): HyperText Markup Language: the language that describes a page's content and structure.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, records, collection expressions, generic types, LINQ, null operators, switch expressions and patterns, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does "parse, don't validate" mean?**

> Instead of checking raw input and passing the same raw strings on, convert it into types that can only hold valid values, such as a `FlightNumber` or a `DateOnly`. Code past that point never needs to check again, because invalid values can't exist.

**2. What is over-posting, also called mass assignment?**

> Binding a request body directly onto an object that has more fields than the client should control, such as a stored booking with a `Fare` or `IsVip` property. A client adds those fields to the JSON and sets them. Bind requests to dedicated request types instead.

**3. Why prefer an allowlist to a denylist?**

> An allowlist accepts only what is known to be valid, such as two letters followed by digits. A denylist tries to reject known-bad input, and attackers are good at finding forms it didn't list.

**4. Should validation stop at the first error?**

> For request input, no. Collect every problem and return them together, grouped by field, so the client can fix everything at once. Business rule checks that depend on each other may still need to stop early.

**5. Where should "is the seat still available?" be checked?**

> In the business logic, against the database, at the moment of booking, inside the atomic update. It can't be checked reliably at the boundary, because it changes between requests.

**6. What is a correlation id for?**

> It is an identifier given to a request at the edge and passed along to every service, log line and response involved. A user reporting an error can quote it, and every related log entry across systems can be found with it.

**7. Why is canonicalization done before validation?**

> The same value can be written many ways: with spaces, different letter case, different Unicode forms. Converting to one standard form first means the check and later comparisons see the same thing, and tricks with alternative forms can't slip past.

### The boundary

A **trust boundary** is any point where data passes from something you don't control to something you do. An HTTP request crossing into your API is the obvious one. So are messages from a queue, files users upload, webhook calls, responses from partner APIs, rows imported from a spreadsheet, and even configuration and environment variables. Inside your service, code can rely on values that have already been checked. At the boundary, it can rely on nothing.

The principle is simple: check everything once, as it crosses, and convert it into trusted forms. Then the inside of the system can be simpler, because it doesn't have to defend against impossible values everywhere.

### Kinds of check

**Input validation** comes in layers, and each belongs in a particular place.

**Syntactic validation** checks form: this is a well-formed date, this is a number, this string is at most 100 characters, this code is two letters followed by digits. It needs nothing but the input itself, so it belongs at the boundary.

**Semantic validation** checks meaning using only the request and simple context: the return date is after the outbound date, the departure date isn't in the past, the passenger count is between 1 and 9. It also belongs at or near the boundary.

Business rules check the request against the current state of the system: this flight exists, this seat is free, this card has credit. They can only be decided by the business logic, against the database, at the moment of the change, often inside the atomic operation from Transactions and isolation (7.4). Checking seat availability at the boundary and then booking later is the check-then-act race again.

### Parse, don't validate

A common approach checks raw input and then passes the same raw strings deeper into the code. Every layer then wonders whether the string was checked, checks again to be safe, or forgets to.

A better approach, summed up in a 2019 essay by Alexis King as **parse, don't validate**, converts input into types that can only hold valid values. A `string` flight number becomes a `FlightNumber`, which can only be created through a method that checks it. A `string` date becomes a `DateOnly`. A seat count becomes an integer known to be in range. The parse either produces the trusted value or reports why it couldn't. Every method that takes a `FlightNumber` knows it is valid, without checking. It is the value object idea from Immutability and pure functions (4.4) applied at the edge, and the strongly typed ids from Types (2.3).

### Rules for checking well

Use an **allowlist**: define what valid input looks like and accept only that. Flight numbers are two letters and one to four digits. Status must be one of a fixed set. A denylist, which tries to reject known-bad input such as "strings containing `<script>`", always misses a variation.

Put the input into one standard form before checking it. This **canonicalization** step trims surrounding spaces, fixes letter case where case doesn't matter, and normalizes Unicode, as Text is numbers too (1.3) described. Otherwise "LHR", "lhr" and " LHR " become three different values, and lookalike characters can pass checks meant to block them.

Limit everything: body size, string lengths, list lengths, nesting depth, numeric ranges, page sizes. Missing limits turn one malicious request into memory exhaustion or a query that runs for minutes.

Report every problem at once, grouped by field, so a user or client developer can fix them all in one go.

### Request types, not entities

Bind each request to a type made for it, often called a **DTO**, data transfer object: a record holding exactly the fields that request may contain, and nothing else. Then map it into domain objects or entities in code.

Binding request JSON straight onto a stored entity invites **over-posting**, also called mass assignment. The entity has properties the client must never set, such as `Fare`, `IsVip` or `OwnerId`. The client adds them to the JSON, and the framework obligingly fills them in. In 2012 this flaw let a researcher add his own key to the Ruby on Rails project on GitHub, by setting a field in a form that the server bound without checking.

Deserializers can be made stricter, too. In C#, the `required` modifier on properties makes deserialization fail when a field is missing. .NET 9 added `RespectNullableAnnotations`, which makes `System.Text.Json` refuse `null` for non-nullable properties, and .NET 8 added `UnmappedMemberHandling.Disallow`, which refuses unknown fields. For requests, strictness catches client mistakes early. For responses you consume, the tolerant-reader advice from API design (8.2) applies instead: ignore fields you don't know.

### Seeing it for real

This program parses booking requests into a trusted command or a complete list of problems, shows over-posting, and shows strict deserialization. It needs no web server; the same code sits behind any endpoint.

```csharp
// validation.cs — run with: dotnet run validation.cs
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);   // camelCase, as ASP.NET Core uses

// 1. Parse, don't validate: raw input becomes a trusted command, or a full list of problems
foreach (string json in new[]
{
    """{"flight":"ba117","date":"2026-09-28","passenger":"  Ada Lovelace ","seats":2}""",
    """{"flight":"BA 117","date":"28/09/2026","passenger":"","seats":12}""",
    """{"flight":"BA117"}""",
})
{
    var request = JsonSerializer.Deserialize<BookingRequest>(json, web)!;
    var (command, errors) = BookingCommand.TryCreate(request, today: new DateOnly(2026, 9, 1));
    Console.WriteLine(command is not null
        ? $"valid: {command.Flight} on {command.Date:yyyy-MM-dd} for {command.Passenger}, {command.Seats} seat(s)"
        : JsonSerializer.Serialize(ValidationProblem(errors), web));
}
// valid: BA117 on 2026-09-28 for Ada Lovelace, 2 seat(s)     <- canonicalized: upper case, trimmed
// {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,
//  "errors":{"flight":["must be two letters and 1 to 4 digits, like BA117"],"date":["must be a date in the form yyyy-MM-dd"],
//            "passenger":["is required"],"seats":["must be between 1 and 9"]}}
// {... "errors":{"date":["is required"],"passenger":["is required"],"seats":["is required"]}}

// 2. Over-posting: binding JSON straight onto a stored entity lets the client set anything it has
string sneaky = """{"passenger":"Mallory","seats":1,"isVip":true,"fare":0}""";
var entity = JsonSerializer.Deserialize<BookingEntity>(sneaky, web)!;
Console.WriteLine($"bound to the entity: VIP {entity.IsVip}, fare {entity.Fare}");   // VIP True, fare 0
var dto = JsonSerializer.Deserialize<BookingRequest>(sneaky, web)!;
Console.WriteLine($"bound to the request type: {dto}");
// BookingRequest { Flight = , Date = , Passenger = Mallory, Seats = 1 }: the extra fields had nowhere to go

// 3. Strict deserialization for requests (.NET 8 and 9 options)
var strict = new JsonSerializerOptions(web)
{
    RespectNullableAnnotations = true,                              // refuse null for non-nullable properties
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,   // refuse fields the type doesn't have
};
foreach (string json in new[] { """{"passenger":null,"seats":1}""", """{"passenger":"Ada","seats":1,"isVip":true}""" })
{
    try { JsonSerializer.Deserialize<StrictRequest>(json, strict); Console.WriteLine("accepted"); }
    catch (JsonException ex) { Console.WriteLine($"rejected: {ex.Message.Split(". ")[0]}"); }
}
// rejected: ... 'Passenger' ... doesn't allow null values
// rejected: The JSON property 'isVip' could not be mapped to any .NET member contained in type 'StrictRequest'

static object ValidationProblem(Dictionary<string, List<string>> errors) => new
{
    type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    title = "One or more validation errors occurred.",
    status = 400,
    errors,
};

record BookingRequest(string? Flight, string? Date, string? Passenger, int? Seats);   // everything optional: it is raw input
record StrictRequest(string Passenger, int Seats);

sealed class BookingEntity                                          // a stored entity: never bind requests to it
{
    public string Passenger { get; set; } = "";
    public int Seats { get; set; }
    public bool IsVip { get; set; }
    public decimal Fare { get; set; } = 450m;
}

sealed record FlightNumber
{
    public string Value { get; }
    FlightNumber(string value) => Value = value;                    // private: the only way in is TryParse

    public static bool TryParse(string text, [NotNullWhen(true)] out FlightNumber? result)
    {
        string t = text.Trim().ToUpperInvariant();                  // canonicalize first
        bool ok = t.Length is >= 3 and <= 6 && char.IsAsciiLetterUpper(t[0]) && char.IsAsciiLetterUpper(t[1])
                  && t[2..].All(char.IsAsciiDigit);                  // an allowlist: exactly this shape, nothing else
        result = ok ? new FlightNumber(t) : null;
        return ok;
    }

    public override string ToString() => Value;
}

sealed record BookingCommand(FlightNumber Flight, DateOnly Date, string Passenger, int Seats)
{
    public static (BookingCommand? Command, Dictionary<string, List<string>> Errors) TryCreate(BookingRequest r, DateOnly today)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);                                       // collect every problem, don't stop at the first
        }

        FlightNumber? flight = null;
        if (string.IsNullOrWhiteSpace(r.Flight)) Add("flight", "is required");
        else if (!FlightNumber.TryParse(r.Flight, out flight)) Add("flight", "must be two letters and 1 to 4 digits, like BA117");

        DateOnly date = default;
        if (string.IsNullOrWhiteSpace(r.Date)) Add("date", "is required");
        else if (!DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            Add("date", "must be a date in the form yyyy-MM-dd");
        else if (date < today) Add("date", "must not be in the past");

        string passenger = r.Passenger?.Trim() ?? "";
        if (passenger.Length == 0) Add("passenger", "is required");
        else if (passenger.Length > 100) Add("passenger", "must be at most 100 characters");

        if (r.Seats is null) Add("seats", "is required");
        else if (r.Seats is < 1 or > 9) Add("seats", "must be between 1 and 9");

        return errors.Count > 0 ? (null, errors) : (new BookingCommand(flight!, date, passenger, r.Seats!.Value), errors);
    }
}
```

### Validation in ASP.NET Core

You rarely write all of this by hand. ASP.NET Core offers several levels of help.

Data annotations, attributes such as `[Required]`, `[StringLength(100)]`, `[Range(1, 9)]` and `[RegularExpression(...)]` on request types, declare simple rules. Controllers marked `[ApiController]` check them automatically and return a 400 validation response before the action runs. Since .NET 10, minimal APIs do the same after calling `builder.Services.AddValidation()`. The FluentValidation library expresses rules in code instead of attributes, which suits complex and conditional rules.

Whatever the tool, keep the pattern from the example: annotations or validators for form and simple meaning at the edge; conversion into domain types; business rules in the domain, against current data.

### Errors going out

The other half of the boundary is what clients get back when something fails. A good **error contract** is consistent across every endpoint, machine-readable, and safe.

Use Problem Details, from Errors and exceptions (2.8), for every error: `type`, `title`, `status` and `detail`, with extra members where useful. ASP.NET Core's validation responses add an `errors` object mapping each field to its messages, the shape the example produces. Give each kind of failure a stable identifier, in `type` or an extra `code` member, such as `flight-full`, so client code can react to it without parsing human-readable text, which you may want to reword or translate.

Choose status codes consistently. Use 400 for malformed or invalid input; some APIs use 422 for input that parses but fails validation, so pick one. Use 401 and 403 for authentication and permission, 404 for missing resources and 409 for conflicts with current state. Use 412 for failed preconditions, 429 for rate limits, and 500 for unexpected failures. Map known exceptions to these in one place, such as an `IExceptionHandler`, as A modern .NET backend (8.3) showed, rather than in every endpoint.

Never reveal internals: no stack traces, SQL, server names, file paths or library versions. They help attackers and confuse users. Log the details on the server instead, and give the client a way to point at them.

### Correlation ids

When a booking fails after passing through a gateway, the booking service, the pricing service and a database, the logs for that one failure are scattered across several systems. A **correlation id** ties them together. It is created at the edge of the system when a request arrives, or taken from the client if it sent one. It is passed along in a header on every call the request causes. It is written into every log line, and returned in the error response.

A user or support agent can then quote it, and every related log line in every system can be found. The W3C Trace Context standard defines a `traceparent` header for exactly this, and .NET's built-in activity tracing propagates it automatically, as Observability (11.6) explains. ASP.NET Core's Problem Details responses already include a `traceId` member for this reason.

### Validating what other systems send you

Responses from partner APIs and other internal services cross a trust boundary too. A partner changes a field's type, sends null where it never did, or sends a date in a new format, and code that trusted the shape fails far from the cause. Parse external responses into your own types at the edge of your system, with the same care as requests, and report or log clearly when they don't fit. Don't let another system's data model spread through your code, which is the adapter idea from Composition and dependency injection (4.3).

### Validation isn't sanitization

Validation decides whether input is acceptable. It doesn't make text safe to insert into SQL, HTML or a shell command. A perfectly valid passenger name can still contain an apostrophe, an angle bracket or a Unicode character that breaks a badly built query or page. Safety comes from how data is used: parameters for SQL, from The relational model and SQL (7.2), encoding for each output context, from Text is numbers too (1.3), and argument lists for processes, from The operating system (1.7). Common web vulnerabilities (10.4) covers this in full.

### In short

Everything that crosses a trust boundary, requests, messages, files and partner responses, must be checked once, on arrival. Check form and simple meaning at the edge, and business rules in the domain against current data. Parse input into types that can only hold valid values, using allowlists, canonicalization and limits, and report every problem together. Bind requests to dedicated DTOs, never to stored entities, to prevent over-posting. Return one consistent, machine-readable error contract with stable codes and no internals, and tie every failure to a correlation id that appears in logs and responses.

### What goes wrong

**Validating deep inside instead of at the edge.** Raw strings travel through the system and each layer checks differently, or not at all. Parse at the boundary into trusted types.

**Binding requests onto entities.** Clients set fields such as prices, roles or owners by adding them to the JSON. Use request types with only the fields a client may send.

**Denylists.** A filter that blocks known-bad patterns misses the next variation. Accept only known-good shapes.

**Checking before canonicalizing.** "LHR" passes and " lhr" is stored as a different airport. Normalize first, then check, then store the normalized form.

**No limits.** A 500 MB JSON body or a list of a million ids takes a server down. Limit sizes, counts and depth everywhere.

**One error at a time.** A form that reports problems one per submission frustrates everyone. Collect and return all field errors together.

**Inconsistent or leaky errors.** Every endpoint returns errors in a different shape, some with stack traces. Use Problem Details everywhere, with stable codes and nothing internal.

**Trusting partner data.** A partner's API changes shape and your system fails in odd places far from the cause. Parse external data at your edge.

### Cost model

Validation is cheap: checking a request with a handful of fields costs microseconds, far less than the database call it may prevent. Deserializing JSON costs roughly microseconds per kilobyte; body size limits keep a single request from costing seconds. Rejecting bad input early saves the work, locks and logs that invalid data would otherwise cost further in. The expensive failures are the missed checks: bad data stored in a database may take days to find and repair.

### In other languages (optional)

**TypeScript.** Zod, Valibot and TypeBox validate at run time and produce the TypeScript types, as A Node and TypeScript backend (8.4) showed. `safeParse` returns either the typed data or all issues.

**Python.** Pydantic models parse and validate input and power FastAPI's request handling and error responses.

**Java.** Bean Validation annotations such as `@NotNull` and `@Size`, checked automatically by Spring.

**Every language.** The pattern is the same everywhere: a schema or type for the request, a parse that yields trusted values or all errors, and one error format.

### Key terms

- **Trust boundary**: A point where data passes from something you don't control into something you do.
- **Input validation**: Checking that incoming data is acceptable before using it.
- **Syntactic validation**: Checking the form of input, such as its type, length and format.
- **Semantic validation**: Checking the meaning of input using the request itself, such as a date not being in the past.
- **Parse, don't validate**: Converting input into types that can only hold valid values, instead of checking and passing on raw data.
- **Allowlist**: A definition of what valid input looks like, accepting only that.
- **Canonicalization**: Converting input to one standard form before checking or comparing it.
- **DTO**: Data transfer object: a type made to carry exactly the data of one request or response.
- **Over-posting**: A client setting fields it shouldn't by adding them to a request bound onto a larger object. Also called mass assignment.
- **Error contract**: The consistent, documented shape and meaning of the errors an API returns.
- **Correlation id**: An identifier attached to a request and passed through every system and log line it touches.

### Where this leads

Making repeated reads fast is in Caching (8.6), and moving slow work out of the request is in Background work and queues (8.7). Validation and escaping as defences against attacks are in Common web vulnerabilities (10.4). Correlation ids and tracing across services are in Observability (11.6). Validating forms in the browser, for convenience only, is in Components and state (9.4).

### Check yourself

1. Add a return date to `BookingRequest` in `validation.cs`, optional, but when present it must be on or after the outbound date. Decide which field the error belongs to.
2. Make `FlightNumber.TryParse` accept a space between letters and digits, such as "BA 117", by canonicalizing it away, and check that "B 117" is still rejected.
3. Add a `code` member to the validation problem, such as `booking-validation-failed`, and a separate `flight-full` problem for the business rule.
4. Find an endpoint in a real app that binds a request to an entity class. List the properties a client could set that it shouldn't.
5. Follow one failed request through the logs of a system you know. Check whether a single id links every log line it produced.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Over-posting that made a user an admin

A profile update endpoint binds the request straight onto the User entity. Someone adds `"isAdmin": true` to the JSON.

*Bind to a DTO with only allowed fields*

```csharp
public sealed record UpdateProfile(string DisplayName, string? Phone);

app.MapPut("/me", async (UpdateProfile dto, ClaimsPrincipal user, AppDb db, CancellationToken ct) =>
{
    var me = await db.Users.SingleAsync(u => u.Id == user.GetUserId(), ct);
    me.DisplayName = dto.DisplayName.Trim();
    me.Phone = dto.Phone;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
```

> **Lesson:** Never bind requests onto stored entities. A DTO lists exactly what the client may change.

#### Example 2: Parsing a booking reference once, at the edge

Booking references are validated with slightly different regexes in five places.

*A value object that can only be valid*

```csharp
public readonly record struct BookingRef
{
    public string Value { get; }
    private BookingRef(string v) => Value = v;

    public static bool TryParse(string? s, out BookingRef r)
    {
        s = s?.Trim().ToUpperInvariant();
        var ok = s is { Length: 6 } && s.All(char.IsAsciiLetterOrDigit);
        r = ok ? new BookingRef(s!) : default;
        return ok;
    }
}
// endpoints parse once; everything downstream takes BookingRef and never re-checks
```

> **Lesson:** Parse input into types that can only hold valid values, at the boundary, and delete the scattered checks.

### Validation & Domain Modeling

<p class="src">From <em>.NET Backend Deep Dive</em> · Validation & Domain Modeling</p>

<a id="dn-146"></a>

#### Three layers of validation, three different jobs

| Layer | Question | Example | Failure |
|---|---|---|---|
| **Input** | Is the request well-formed? | Required fields, ranges, formats | 400 |
| **Business rule** | Is this allowed given current state? | "Can't cancel a shipped order" | 409 / 422 |
| **Invariant** | Can the object even exist like this? | "Order total equals sum of lines" | Should be impossible |

Input validation belongs at the edge. Business rules belong in the application/domain layer, because they need state. Invariants belong **inside the type**, enforced by the constructor.

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();

public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().Must(i => i.Count <= 100)
            .WithMessage("Maximum 100 line items");
        RuleForEach(x => x.Items).ChildRules(i =>
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, 999));
    }
}
```

FluentValidation over DataAnnotations once rules become conditional or cross-field — attributes stop scaling quickly. Run it as a filter so handlers never see invalid input, and map failures to a ProblemDetails response with a per-field `errors` dictionary.

<a id="dn-147"></a>

#### Make illegal states unrepresentable

```csharp
// Primitive obsession: every one of these is just a string
void Transfer(string from, string to, decimal amount, string currency);
Transfer(to, from, amount, currency);   // compiles perfectly, wrong forever
```

```csharp
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0) throw new ArgumentException("Negative amount");
        if (currency.Length != 3) throw new ArgumentException("ISO 4217 code required");
        Amount = amount; Currency = currency;
    }

    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency
            ? new(a.Amount + b.Amount, a.Currency)
            : throw new InvalidOperationException("Currency mismatch");
}
```

Now `Transfer(OrderId, CustomerId, Money)` cannot be called with the arguments swapped, and adding USD to EUR is a runtime failure at the point of the mistake rather than a wrong number three screens later. Value objects are exactly what `readonly record struct` is for.

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a66"></a>

#### Validation, error handling and ProblemDetails

Validate in layers: shape at the edge (is this a valid email string?), invariants in the domain (can this order be cancelled?). Do not put business rules in DTO attributes, and do not re-validate strings in the domain.

``` csharp
// edge validation with FluentValidation
public class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
    }
}

// one place that turns exceptions into RFC 9457 responses
app.UseExceptionHandler(a => a.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        DomainException      => (StatusCodes.Status409Conflict,   "Business rule violated"),
        NotFoundException    => (StatusCodes.Status404NotFound,   "Resource not found"),
        _                    => (StatusCodes.Status500InternalServerError, "Unexpected error")
    };
    await Results.Problem(title: title, statusCode: status,
        extensions: new Dictionary<string, object?> { ["traceId"] = Activity.Current?.Id })
        .ExecuteAsync(ctx);
}));
```

> **\[Note\]**
>
> **Exceptions vs results.** Use exceptions for genuinely exceptional conditions and for anything that must abort a transaction. Use a `Result<T>` type for expected failures on hot paths — throwing is roughly a thousand times more expensive than returning.

ASP.NET Core gives you the whole error path already: a `ProblemDetails` shape (RFC 9457), automatic 400s for model-binding failures, and since .NET 8 a proper handler pipeline. Wire it once, centrally, and endpoints stop carrying try/catch noise.

``` csharp
// Program.cs — one place, every unhandled error
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
    ctx.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.TraceId.ToString();     // ← the ONE field support needs
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
app.UseExceptionHandler();

public sealed class DomainExceptionHandler(IProblemDetailsService problems)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx,
        Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException      => (400, "Validation failed"),
            NotFoundException        => (404, "Resource not found"),
            ConcurrencyException     => (409, "Conflicting update"),
            _                        => (0, ""),      // not ours: let it bubble
        };
        if (status == 0) return false;

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new()
        {
            HttpContext = ctx,
            ProblemDetails = { Status = status, Title = title,
                               Detail = ex.Message },   // safe messages only
        });
    }
}
```

> **\[Trap\]**
>
> **Common trap.** Never put `ex.ToString()` or an inner exception message into the response. Stack traces name your types, paths and dependency versions — that is reconnaissance for an attacker and noise for a client. Log the full exception with the trace id; return the id and a sentence.

> **\[Trap\]**
>
> **Validation belongs at the edge *and* in the domain.** Attributes or FluentValidation reject malformed input before it reaches your code; the aggregate still refuses to move to an illegal state on its own. The two are not duplication — one protects the API contract, the other protects the invariant. Only the second survives a new caller.

### The data layer as an architecture

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The data layer as an architecture</p>

<a id="be-m266"></a>

#### Value objects and parsing at the boundary

*Primitive obsession* is representing a domain concept with a bare primitive. It compiles fine and it lets you pass the wrong thing.

``` csharp
// Nothing here stops you swapping the arguments. The bug is silent and permanent.
void Transfer(int fromAccountId, int toAccountId, decimal amount, string currency);
Transfer(toId, fromId, amount, "USD");
```

``` csharp
// Strongly typed ids: a one-line struct, zero runtime cost, impossible to confuse.
public readonly record struct OrderId(long Value) {
    public override string ToString() => $"order/{Value}";
    public static OrderId Parse(string s) => new(long.Parse(s.AsSpan()["order/".Length..]));
}

// Teach EF to store it as the underlying primitive:
modelBuilder.Entity<Order>()
    .Property(o => o.Id)
    .HasConversion(id => id.Value, v => new OrderId(v));

// Or globally, so every occurrence maps the same way:
configurationBuilder.Properties<OrderId>().HaveConversion<OrderIdConverter>();
```

``` csharp
// A value object that carries an invariant - it cannot exist in an invalid state.
public readonly record struct Money : IComparable<Money> {
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency) {
        if (currency is not { Length: 3 }) throw new ArgumentException(nameof(currency));
        Amount = amount; Currency = currency.ToUpperInvariant();
    }

    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency ? new(a.Amount + b.Amount, a.Currency)
                                 : throw new InvalidOperationException("Currency mismatch");
    public int CompareTo(Money other) =>
        Currency == other.Currency ? Amount.CompareTo(other.Amount)
                                   : throw new InvalidOperationException("Currency mismatch");
}
```

``` csharp
// Parse at the boundary, not throughout. ISpanParsable makes it uniform
// and generic-friendly (§61).
public readonly record struct Sku : ISpanParsable<Sku> {
    private readonly string _value;
    private Sku(string v) => _value = v;

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? _, out Sku result) {
        result = default;
        if (s.Length is < 3 or > 50) return false;
        foreach (var c in s) if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        result = new Sku(s.ToString());
        return true;
    }
    public static Sku Parse(ReadOnlySpan<char> s, IFormatProvider? p) =>
        TryParse(s, p, out var r) ? r : throw new FormatException($"Invalid SKU");
    // ... string overloads delegate to these
}
```

> **\[Why it matters\]**
>
> **The payoff is not elegance, it is deletion.** Once a `Sku` can only be constructed from valid input, every downstream validation check, every defensive null-or-empty test, and every "is this a SKU or a product name?" comment goes away. The type carries the guarantee. This is the same argument as making a column `NOT NULL` ([Constraints: what the database guarantees](07-02-the-relational-model-and-sql.md#be-m188)) — push the invariant to the edge and the interior gets simpler.

> **\[Trap\]**
>
> **Keep them small and `readonly`.** A value object wrapping one primitive costs nothing when it is a `readonly record struct`. Make it a class and you have added an allocation and a null check to every use; make it a mutable struct and you have added the copy bugs from [Value types vs reference types](02-04-values-variables-and-references.md#be-c306). Also verify the EF conversion actually translates — a converted property can lose comparison semantics in SQL ([Type mapping: CLR to SQL](07-07-orms-and-ef-core.md#be-m170)).

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

Applied in: [Entities, value objects, and invariants](12-06-domain-modeling-and-application-architecture.md#be-m254)
