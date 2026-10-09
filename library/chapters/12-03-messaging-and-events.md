---
id: "12-3"
number: "12.3"
title: "Messaging and events"
part: 12
part_title: "Systems and architecture"
words: 14058
minutes: 61
---

# 12.3 Messaging and events

> **Quick look.** Messaging and events: decoupling services in time, failure and knowledge.
>
> - Commands ask one handler to act; events state past-tense facts for any number of listeners.
> - Publishing outside the transaction loses events: use a transactional outbox or change data capture.
> - Delivery is at least once, so consumers must be idempotent (record processed message ids).
> - Ordering holds only per partition or key; choose the key carefully to avoid hot partitions.
> - Kafka is a replayable log, RabbitMQ a router (set prefetch, ack after processing), SQS is simple (watch the visibility timeout).
> - Sagas replace distributed transactions with local steps and compensations, by choreography or orchestration.
> - Alert on dead-letter queue depth and message age; a database table with SKIP LOCKED is a fine queue at moderate volume.
> - Event sourcing and CQRS are powerful but expensive; use them only where they pay.


### The big picture

When a passenger books a flight, several other parts of an airline need to know. The email service sends a confirmation, loyalty awards points, revenue accounting records the sale, and analytics counts it. If the booking service called each of them in turn, it would depend on all of them. It would be slow when any was slow and failing when any failed, and it would need changing every time a new one was added. Events turn this around. The booking service announces a fact, "booking created", and anything that cares subscribes to it. The publisher doesn't know or care who is listening.

This chapter covers messaging between services and modules, building on the queues from Background work and queues (8.7) and the outbox from Transactions and isolation (7.4). It explains commands versus events, the two shapes of event, how to design and version events that other teams depend on, and how ordering and reliable publishing work. It then covers three patterns built on events. Sagas carry out a business transaction across services, and undo it when a step fails. Event sourcing stores the events themselves as the source of truth. And CQRS separates the model that changes data from the models that read it. The example simulates all of them in memory, run with Node.

### Before you start

**Builds on:** 8.7 Background work and queues, 7.4 Transactions and isolation, 11.6 Observability, 12.2 Distributed systems basics, 8.2 API design, 12.1 Monoliths, modules and services.

**Ideas explained later:**

- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Performance** (12.4): How quickly a system does its work at a given load.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between a command and an event?**

> A command asks one recipient to do something, and may be refused: "book seat 12A". An event states a fact that already happened, in the past tense, for any number of listeners, and can't be refused: "seat 12A booked".

**2. What is the difference between event notification and event-carried state transfer?**

> A notification says only that something changed, with an id, and consumers call back for details. Event-carried state transfer includes the data consumers need, so they don't call back, at the cost of larger events and copies of data in many places.

**3. How do you change an event's structure when old events are still in queues and logs?**

> Make additive changes where possible, give each event a version, and have consumers upgrade old versions as they read them, called upcasting. A truly incompatible change becomes a new event type.

**4. What is a saga?**

> A business transaction spread across services, made of local steps, each with a compensating action. If a later step fails, the saga runs the compensations for the completed steps in reverse order, instead of relying on a distributed transaction.

**5. What is the difference between saga choreography and orchestration?**

> In choreography, each service reacts to the others' events, with no central coordinator. In orchestration, one coordinator tells each service what to do next and tracks the saga's state. Orchestration is easier to follow; choreography couples services less.

**6. What is event sourcing?**

> Storing every change as an event, and treating that list of events as the source of truth. Current state is computed by replaying the events, so the full history is kept and state can be reconstructed for any past moment.

**7. What does CQRS separate?**

> The model that handles commands and enforces rules from the models that answer queries. Read models can be shaped for each screen and scaled separately, and are usually updated from events, so they are eventually consistent.

### Commands and events

Messages between parts of a system come in two kinds, and confusing them causes design problems.

A **command** asks one recipient to do something: "book seat 12A on BA117", "send this email". It is named in the imperative, it has exactly one handler, and that handler may refuse it. Sending a command couples the sender to the fact that someone will do the job.

An **event** records a fact that has already happened: "seat 12A booked", "payment captured". It is named in the past tense, any number of listeners may receive it, and it can't be refused, because it is history. The publisher doesn't know who listens. That is the source of events' main benefit, and their main difficulty.

### Event-driven architecture

In an **event-driven architecture**, parts of a system react to each other's events instead of calling each other directly. It works through **publish-subscribe**: a publisher sends each event to a topic, from Background work and queues (8.7), and the broker delivers a copy to every subscription. Adding a new consumer, such as a fraud-scoring service, needs no change to the publisher at all.

The benefits are decoupling in several senses. In time: consumers process events when they can, and the publisher doesn't wait. In failure: a crashed consumer delays its own work but nobody else's, as the example's first simulation shows. And in knowledge: the publisher doesn't need to know who consumes, or why.

The costs are real. A business process spread across several services' reactions is hard to see as a whole; distributed tracing, from Observability (11.6), becomes essential, which is why trace context travels in message headers. Consumers see changes a little later, the eventual consistency from Distributed systems basics (12.2). And every consumer must handle duplicates and cope with messages arriving out of order, from Background work and queues (8.7).

### Thin and fat events

Events come in two broad shapes.

An **event notification** carries little more than what happened and an id: "booking B7 created". Consumers that need details call the publisher's API. Events stay small and never contain stale copies of data, but the publisher gets a burst of calls after every event, and consumers depend on it being available.

**Event-carried state transfer** puts the data consumers need into the event itself: the flight, the passenger, the fare. Consumers can act without calling back, and can keep their own local copy of what they need. Events get larger, the same data is copied into many services, and personal data spreads further, which matters for privacy rules such as the GDPR. Many systems use fat events for data that other contexts genuinely need, and thin events for the rest.

### Designing events

An event is a contract, as binding as an API from API design (8.2), because other teams build on it. A few conventions make events dependable.

Name events in the past tense, in the language of their bounded context, from Monoliths, modules and services (12.1): `BookingCreated`, `SeatReleased`. Give every event an id, which consumers use for duplicate detection. Also give it a type, a version, the time it occurred, and correlation and trace ids, so an event can be followed through the system.

Distinguish the events a context uses internally from the ones it publishes to others. A **domain event** is a fact inside one bounded context, which its own code reacts to, and it can change as the context's model changes. An **integration event** is published for other contexts, and must stay stable: it is a public contract, versioned and documented, and it often carries a deliberately smaller, simpler shape than the internal model.

Events outlive the code that wrote them: they sit in queues, logs and event stores for months. So versioning follows the same rules as APIs. Add fields rather than renaming or removing them. Consumers ignore fields they don't know, the tolerant reader from API design (8.2). Put a version in every event, and have consumers convert old versions to the current shape as they read them, called **upcasting**, as the example's second simulation does. For changes that can't be made compatibly, publish a new event type alongside the old one, and retire the old one once nothing uses it. Streaming platforms often use a **schema registry**, a service that stores each event type's schema and rejects incompatible changes before they are published.

### Order and reliability

Events about the same thing usually need to be processed in order: a booking's "created" before its "cancelled". Global ordering across everything is expensive and rarely needed. Brokers instead order events per key, in Kafka's partitions or Service Bus sessions, so all events for one booking go to one consumer in order, while events for different bookings spread across many, the partition keys from Non-relational stores (7.6).

Publishing must be reliable in the sense of Transactions and isolation (7.4): the state change and its event must both happen or neither. The transactional outbox does that, writing the event into the same database transaction as the change and publishing it afterwards. Another option is **change data capture**, which reads the database's own transaction log and turns committed changes into events, with tools such as Debezium. Either way delivery is at least once, so consumers are idempotent, from Background work and queues (8.7).

Queues and event streams store events differently. A queue deletes a message once it is processed. An event stream, such as Kafka or Azure Event Hubs, keeps events for a configured time, or forever, as an ordered log. Each **consumer group** tracks its own position in the log, so a new consumer can start from the beginning and replay history, and a buggy consumer can be fixed and rerun.

### Sagas

A booking that holds a seat, charges a card and issues a ticket touches three services. There is no transaction across them: distributed transactions, from Transactions and isolation (7.4), are slow, fragile and unsupported by most modern infrastructure. A **saga** is the alternative. It breaks the business transaction into local steps, each a normal transaction in one service, and gives each step a **compensating action** that semantically undoes it. If a step fails, the saga runs the compensations for the steps already completed, in reverse order. A declined card releases the held seat. A ticketing failure after the charge refunds the card and releases the seat, exactly as the example's third simulation shows.

Compensation is not the same as rollback. A refund isn't the same as never charging: the passenger may see both on a statement. And some actions can't be undone at all, such as an email already sent. Sagas therefore order steps carefully, doing the ones that are easiest to undo first and the irreversible ones last, and use pending states, such as a seat "held" rather than "sold", to show that a saga is still in progress.

There are two ways to coordinate a saga. In **saga choreography**, there is no coordinator: each service listens for the previous step's event and publishes its own, and failures are signalled with events that trigger compensations. It couples services least, and the overall flow exists only implicitly, spread across services. In **saga orchestration**, one coordinator sends each step's command, waits for the result, and decides what to do next, including compensations. The flow is explicit in one place and easy to monitor, at the cost of a central component. Longer or more complex sagas usually benefit from orchestration. Durable workflow tools such as Temporal, Azure Durable Functions and the saga support in MassTransit and NServiceBus persist an orchestrator's state, so a saga survives crashes and restarts.

### Event sourcing

Most systems store current state: a loyalty account's balance is a number in a row, overwritten on every change. **Event sourcing** stores the changes instead. The account is its sequence of events, `PointsEarned`, `PointsRedeemed`, `PointsExpired`, appended and never altered. Its current balance is computed by replaying them, which is a fold, the `reduce` from Immutability and pure functions (4.4).

The benefits are strongest where history matters. There is a complete audit trail for free. State can be reconstructed for any past moment, "what was the balance on 1 May?", as the example's fourth simulation does. A bug in how state was computed can be fixed and the state rebuilt from the events. And new views of old data can be built at any time. Ledgers, such as loyalty points, payments and seat inventory, suit it well.

The costs are real too. Events are kept forever, so their versioning must be handled forever. Queries need separate read models, because "all members with over 10,000 points" can't be answered by replaying every account. Long streams need periodic snapshots, from Immutability and pure functions (4.4), so loading an account doesn't replay years of events. And privacy rules that require deleting personal data need special techniques, such as encrypting each person's data with their own key and deleting the key. Most parts of most systems are better served by storing current state.

### CQRS

**CQRS**, command query responsibility segregation, separates the model that changes data from the models that read it. The write side handles commands, enforces the rules and records changes, as the example's `redeem` does when it refuses to redeem more points than exist. The read side offers one or more **read models**: data structures shaped for particular screens or queries, such as a table with each member's balance and last activity for the account page, updated from the write side's events. Read models can be denormalized, from Schema design and migrations (7.5), stored in whatever fits best, a search index, a cache or a document store, and scaled separately from writes.

CQRS pairs naturally with event sourcing, but doesn't require it: a normal database with separate query models, or even separate EF Core read queries, from ORMs and EF Core (7.7), is a mild form of it. Its cost is that read models lag slightly behind writes, so screens must tolerate eventual consistency, or show the user's own change directly after they make it, the read-your-writes idea from Non-relational stores (7.6). Use it where reads and writes genuinely differ in shape or scale, not by default.

### Seeing it for real

This script simulates each idea in this chapter in memory: publish-subscribe with one failing subscriber, upcasting an old event version, a saga with compensations, an event-sourced loyalty account with a query for a past date, and a CQRS read model.

```typescript
// events.ts: run with: node events.ts   (publish-subscribe, event versions, a saga and event sourcing, in memory)

// 1. Publish-subscribe: the publisher doesn't know who listens; each subscriber succeeds or fails on its own
type BookingCreated = { type: "BookingCreated"; version: 2; bookingId: string; flight: string; passenger: { name: string; loyaltyId?: string } };
const subscribers: Record<string, (e: BookingCreated) => void> = {
  email: e => console.log(`   email: confirmation for ${e.passenger.name} on ${e.flight}`),
  loyalty: e => { if (!e.passenger.loyaltyId) throw new Error("no loyalty id"); console.log(`   loyalty: points for ${e.passenger.loyaltyId}`); },
  analytics: e => console.log(`   analytics: booking ${e.bookingId} counted`),
};
function publish(event: BookingCreated) {
  for (const [name, handle] of Object.entries(subscribers)) {
    try { handle(event); }
    catch (err) { console.log(`   ${name}: failed (${(err as Error).message}); retried later, the others are unaffected`); }
  }
}
console.log("1. one event, three independent subscribers");
publish({ type: "BookingCreated", version: 2, bookingId: "B7", flight: "BA117", passenger: { name: "Ada" } });

// 2. Schema evolution: old events stay in queues and logs, so consumers upgrade them as they read
type BookingCreatedV1 = { type: "BookingCreated"; version: 1; bookingId: string; flight: string; passengerName: string };
const upcast = (e: BookingCreatedV1 | BookingCreated): BookingCreated =>
  e.version === 2 ? e : { type: e.type, version: 2, bookingId: e.bookingId, flight: e.flight, passenger: { name: e.passengerName } };
const stored = JSON.parse('{"type":"BookingCreated","version":1,"bookingId":"B3","flight":"QF1","passengerName":"Grace","promo":"x"}');
console.log("2. a version 1 event from last year, read by today's code:", JSON.stringify(upcast(stored).passenger));   // {"name":"Grace"}; the unknown "promo" is ignored

// 3. A saga: steps across services, each with a compensating action if a later step fails
type Step = { name: string; run: () => void; undo: () => void };
function saga(steps: Step[]) {
  const done: Step[] = [];
  for (const step of steps) {
    try { step.run(); done.push(step); console.log(`   ${step.name}: done`); }
    catch (err) {
      console.log(`   ${step.name}: failed (${(err as Error).message}), compensating`);
      for (const d of done.reverse()) { d.undo(); console.log(`   undo ${d.name}`); }
      return "cancelled";
    }
  }
  return "completed";
}
const trip = (declineCard: boolean, ticketingDown: boolean): Step[] => [
  { name: "hold seat 12A", run: () => {}, undo: () => {} },
  { name: "charge card", run: () => { if (declineCard) throw new Error("card declined"); }, undo: () => {} },
  { name: "issue ticket", run: () => { if (ticketingDown) throw new Error("ticketing unavailable"); }, undo: () => {} },
];
console.log("3. saga, card declined:");
console.log(`   result: ${saga(trip(true, false))}`);
console.log("   saga, ticketing fails after the charge:");
console.log(`   result: ${saga(trip(false, true))}`);        // undo charge card (a refund), then undo hold seat 12A

// 4. Event sourcing: the account is its list of events; the balance is computed from them
type LoyaltyEvent = { type: "Earned" | "Redeemed" | "Expired"; points: number; on: string };
const account: LoyaltyEvent[] = [
  { type: "Earned", points: 1200, on: "2026-01-10" },
  { type: "Earned", points: 800, on: "2026-03-02" },
  { type: "Redeemed", points: 1500, on: "2026-06-20" },
  { type: "Expired", points: 200, on: "2026-09-01" },
];
const balance = (events: LoyaltyEvent[], asOf = "9999-12-31") =>
  events.filter(e => e.on <= asOf).reduce((sum, e) => sum + (e.type === "Earned" ? e.points : -e.points), 0);
function redeem(events: LoyaltyEvent[], points: number, on: string) {   // the write side checks rules against the events
  if (points > balance(events)) throw new Error(`only ${balance(events)} points available`);
  events.push({ type: "Redeemed", points, on });
}
console.log(`4. balance now ${balance(account)}, on 2026-05-01 it was ${balance(account, "2026-05-01")}`);   // 300; 2000
try { redeem(account, 500, "2026-09-30"); } catch (err) { console.log(`   redeem 500: refused, ${(err as Error).message}`); }

// 5. CQRS: a separate read model, updated from the same events, shaped for one screen
const readModel = new Map<string, { balance: number; lastActivity: string }>();
for (const e of account) {
  const row = readModel.get("ada") ?? { balance: 0, lastActivity: "" };
  readModel.set("ada", { balance: row.balance + (e.type === "Earned" ? e.points : -e.points), lastActivity: e.on });
}
console.log("5. read model for the account page:", JSON.stringify(readModel.get("ada")));   // {"balance":300,"lastActivity":"2026-09-01"}
```

Its output:

```text
1. one event, three independent subscribers
   email: confirmation for Ada on BA117
   loyalty: failed (no loyalty id); retried later, the others are unaffected
   analytics: booking B7 counted
2. a version 1 event from last year, read by today's code: {"name":"Grace"}
3. saga, card declined:
   hold seat 12A: done
   charge card: failed (card declined), compensating
   undo hold seat 12A
   result: cancelled
   saga, ticketing fails after the charge:
   hold seat 12A: done
   charge card: done
   issue ticket: failed (ticketing unavailable), compensating
   undo charge card
   undo hold seat 12A
   result: cancelled
4. balance now 300, on 2026-05-01 it was 2000
   redeem 500: refused, only 300 points available
5. read model for the account page: {"balance":300,"lastActivity":"2026-09-01"}
```

In a real system each piece is distributed. The bus is a broker with topics and subscriptions. The saga's steps are commands to other services, with the saga's progress stored durably. The event store is a database table or a dedicated store, and the read model is updated by a consumer of the events, a moment after each change.

### Tools in .NET

Azure Service Bus provides queues, topics and sessions; Azure Event Hubs and Kafka provide event streams; and Azure Event Grid routes events between Azure services and applications. Libraries such as MassTransit, NServiceBus and Wolverine sit on top of brokers. They handle serialization, retries, dead-lettering, the outbox and sagas, which saves writing that plumbing for every service. For event sourcing, Marten stores events and read models in PostgreSQL, and EventStoreDB is a database built for it.

### In short

Commands ask one handler to act and may be refused; events state facts for any number of listeners. Event-driven architecture uses publish-subscribe to decouple services in time, failure and knowledge, at the cost of harder-to-follow flows, eventual consistency, duplicates and ordering. Events are thin notifications or carry state; name them in the past tense, keep integration events stable, version them, and upcast old ones. Order per key, publish through an outbox or change data capture, and make consumers idempotent. Sagas replace distributed transactions with local steps and compensations, coordinated by choreography or orchestration. Event sourcing stores events as the truth, for audit and history, and CQRS separates writes from read models. Use each where it pays for itself.

### What goes wrong

**Commands disguised as events.** "SendConfirmationEmail" published as an event still couples the publisher to one consumer's job. Publish facts, and let consumers decide what to do.

**Breaking event changes.** A renamed field breaks every consumer, and old events in the log break new code. Change additively, version events and upcast.

**Internal models published as contracts.** Every refactoring of a context's model breaks other teams. Publish separate, stable integration events.

**Publishing outside the transaction.** The database change commits and the event is lost, or the event goes out for a change that rolled back. Use an outbox or change data capture.

**Assuming order or exactly-once delivery.** A cancellation is processed before its booking, or points are awarded twice. Order per key, and make consumers idempotent.

**Sagas without compensations.** A failed step leaves a charged card and no ticket. Design each step's compensation first, and order irreversible steps last.

**Event sourcing everywhere.** Simple data becomes streams, projections and versioning forever. Use it for ledgers and audit-heavy domains.

**Read models treated as immediately consistent.** A user changes something and the screen shows the old value. Show their own change directly, or wait for the read model to catch up.

### Cost model

Publishing an event costs a broker write, a few milliseconds, typically inside the outbox's transaction and relay rather than the user's request. Delivery to consumers usually takes milliseconds to seconds, which is the delay users see before read models and other services catch up. Each subscriber gets its own copy, so fat events multiply storage and bandwidth by the number of subscribers. Event stores grow forever; snapshots cap the cost of loading long streams. The largest costs are human: flows spread across services are harder to understand and debug, which tracing and good event design offset.

### In other languages (optional)

**Java.** Spring Cloud Stream and the Kafka clients; Axon Framework supports event sourcing, CQRS and sagas.

**Node.js and TypeScript.** Kafka, RabbitMQ and cloud broker clients, with libraries such as KafkaJS; event sourcing is usually built on a database or EventStoreDB's client.

**Every stack.** CloudEvents is a vendor-neutral specification for describing events, with standard fields such as id, source, type and time, which many brokers and cloud services support.

### Key terms

- **Command**: A message asking one recipient to do something, which it may refuse.
- **Event**: A message stating a fact that has already happened, for any number of listeners.
- **Event-driven architecture**: A design where parts of a system react to each other's events instead of calling each other.
- **Publish-subscribe**: Sending messages to a topic so every subscriber receives a copy, without the publisher knowing them.
- **Event notification**: A small event saying something changed, with an id, so consumers fetch details themselves.
- **Event-carried state transfer**: An event that includes the data consumers need, so they don't call back.
- **Domain event**: A fact inside one bounded context, which its own code reacts to.
- **Integration event**: A stable, versioned event published for other bounded contexts.
- **Upcasting**: Converting an old version of an event into the current shape as it is read.
- **Schema registry**: A service that stores event schemas and rejects incompatible changes.
- **Change data capture**: Turning committed database changes into events by reading the database's transaction log.
- **Consumer group**: A set of consumers sharing one position in an event stream.
- **Saga**: A business transaction across services, made of local steps with compensating actions.
- **Compensating action**: A step that semantically undoes an earlier step of a saga, such as a refund.
- **Saga choreography**: Coordinating a saga by having each service react to the previous step's event.
- **Saga orchestration**: Coordinating a saga with a central component that directs each step.
- **Event sourcing**: Storing every change as an event and computing state by replaying them.
- **CQRS**: Command query responsibility segregation: separating the model that changes data from the models that read it.
- **Read model**: A data structure shaped for particular queries, updated from the write side's changes.

### Where this leads

Handling more load, including by processing events in parallel across partitions, is in Scaling and performance (12.4). Keeping consumers and publishers running through failures is in Reliability (12.5). The delivery guarantees events rely on were in Background work and queues (8.7).

### Check yourself

1. In `events.ts`, make the email step of a new saga irreversible, and reorder the trip's steps so that it runs last. Explain why.
2. Add a version 3 of `BookingCreated` with a `cabin` field, defaulting to "economy" when upcasting older versions.
3. Add a `PointsTransferred` event type to the loyalty account and update both `balance` and the read model to handle it.
4. Design the events for seat selection, from browsing a seat map to confirming a seat. Mark each as a domain or integration event, and thin or fat.
5. Find a place in a system you know where one service calls several others after a change, and sketch how events would change it, including what becomes eventually consistent.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Duplicate loyalty points from redelivered messages

The broker redelivers `BookingCompleted` after a consumer timeout, and members get points twice.

*Idempotent consumer with an inbox*

```csharp
public async Task Consume(ConsumeContext<BookingCompleted> ctx)
{
    var id = ctx.MessageId!.Value;
    await using var tx = await db.Database.BeginTransactionAsync(ctx.CancellationToken);
    db.Inbox.Add(new InboxRecord(id, nameof(BookingCompleted)));   // unique index on MessageId
    try { await db.SaveChangesAsync(ctx.CancellationToken); }
    catch (DbUpdateException) when (IsUniqueViolation()) { return; }   // already processed

    await loyalty.AwardAsync(ctx.Message.CustomerId, ctx.Message.Amount, ctx.CancellationToken);
    await db.SaveChangesAsync(ctx.CancellationToken);
    await tx.CommitAsync(ctx.CancellationToken);                       // inbox row and points commit together
}
```

> **Lesson:** Delivery is at least once. Record message ids in the same transaction as the work so a repeat does nothing.

#### Example 2: A booking saga with compensation

Booking reserves a seat, charges a card, then issues a ticket. If ticketing fails, the charge and seat must be undone.

*Orchestrated steps and compensations*

```text
ReserveSeat      -> ok            (compensate: ReleaseSeat)
ChargeCard       -> ok            (compensate: RefundCharge)
IssueTicket      -> FAILED
  RefundCharge   -> ok   (idempotent, retried until done, then alerts a human)
  ReleaseSeat    -> ok
Booking status: Pending -> Failed, customer notified
```

> **Lesson:** Without distributed transactions, each step needs an idempotent compensation, and the intermediate states must be visible in the domain.

### Message Queue vs Message Bus

<p class="src">From <em>.NET Backend Deep Dive</em> · Message Queue vs Message Bus</p>

<a id="dn-12"></a>

#### The distinction

| | **Message Queue** | **Message Bus / Broker (pub-sub)** |
|---|---|---|
| Model | Point-to-point | Publish–Subscribe |
| Receivers | **One** consumer gets the message | **Many** subscribers each get a copy |
| Semantics | **Command** — "do this" | **Event** — "this happened" |
| Coupling | Producer knows who handles it | Producer doesn't know who listens |
| Examples | SQS, RabbitMQ queue, Azure Queue Storage | Kafka, Azure Service Bus Topic, RabbitMQ exchange, SNS |

In practice "message bus" also has a second meaning: an application-level abstraction library (MassTransit, NServiceBus, Rebus, Wolverine) that handles serialization, retries, DLQs, sagas and the outbox for you.

<a id="dn-13"></a>

#### Command vs Event — a design decision

```csharp
// COMMAND: an instruction, one handler, imperative verb
public record ChargePayment(Guid OrderId, decimal Amount);   // -> queue

// EVENT: a statement of fact, N handlers, past tense
public record OrderPlaced(Guid OrderId, Guid CustomerId, decimal Total);  // -> topic
// Listeners: email service, inventory, analytics, loyalty...
```

<a id="dn-14"></a>

#### Practical — MassTransit

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedEmailConsumer>();
    x.AddConsumer<OrderPlacedInventoryConsumer>();

    // Outbox: publish and DB commit succeed or fail together
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq://localhost");
        cfg.UseMessageRetry(r => r.Exponential(
            5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(ctx);   // queues named after consumers
    });
});
```

```csharp
public class OrderPlacedEmailConsumer : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> ctx)
    {
        var msg = ctx.Message;
        // MUST be idempotent: brokers only guarantee at-least-once
        if (await _store.AlreadyProcessedAsync(ctx.MessageId!.Value)) return;

        await _email.SendOrderConfirmationAsync(msg.OrderId);
        await _store.MarkProcessedAsync(ctx.MessageId.Value);
    }
}
```

<a id="dn-15"></a>

#### Things you must handle

1. **At-least-once → consumers must be idempotent.** Persist processed `MessageId`s (Redis, or a table with a unique index).
2. **The dual-write problem**: you `SaveChanges()` then `Publish()` — if the publish fails, the database changed but the event vanished. → **Transactional outbox**: write the event to an `Outbox` table in the same transaction, and have a worker publish from it.
3. **Dead letter queue**: after N failures, park the message, alert, and provide a replay tool.
4. **Poison messages**: one bad message can block the whole queue → bounded retries plus DLQ.
5. **Ordering**: Kafka guarantees order within a **partition**; SQS FIFO within a **message group**. Plain queues generally guarantee nothing.
6. **Versioning**: add optional fields only; never remove or retype existing ones. Producers and consumers deploy at different times.

---

### Resilience & Distributed Systems Patterns

<p class="src">From <em>.NET Backend Deep Dive</em> · Resilience & Distributed Systems Patterns</p>

<a id="dn-51"></a>

#### Sagas — distributed transactions without 2PC

You can't have ACID across services. Replace it with a chain of local transactions plus **compensating actions**.

```
Place order:
  1. OrderService: create order (Pending)
  2. PaymentService: charge          -> fails? compensate: cancel the order
  3. InventoryService: reserve stock -> fails? compensate: refund + cancel
  4. ShippingService: create shipment-> fails? compensate: restock + refund + cancel
  5. OrderService: order -> Confirmed
```

**Choreography** (each service reacts to others' events): simple with few steps, but nobody sees the whole flow and it degenerates into event spaghetti.
**Orchestration** (a saga coordinator drives it): explicit, debuggable, easy to extend. MassTransit provides `MassTransitStateMachine`.

**The critical constraint:** compensating actions must be **idempotent** and **eventually succeed** (retry forever). A refund can fail — so you need a DLQ and a manual remediation process.

<a id="dn-52"></a>

#### Outbox + inbox patterns

```csharp
// OUTBOX: solves the dual-write problem (DB commit vs message publish)
using var tx = await db.Database.BeginTransactionAsync(ct);
db.Orders.Add(order);
db.OutboxMessages.Add(new OutboxMessage
{
    Id = Guid.NewGuid(),
    Type = nameof(OrderPlaced),
    Payload = JsonSerializer.Serialize(new OrderPlaced(order.Id, order.Total)),
    OccurredAt = DateTime.UtcNow
});
await db.SaveChangesAsync(ct);      // one transaction: order and message commit together
await tx.CommitAsync(ct);

// A separate worker reads the Outbox table, publishes, and stamps ProcessedAt
```

```csharp
// INBOX: on the consumer side, prevents duplicate processing
// The table has a UNIQUE INDEX on MessageId
try
{
    db.InboxMessages.Add(new InboxMessage { MessageId = ctx.MessageId!.Value });
    await db.SaveChangesAsync(ct);     // duplicate -> unique violation -> skip
}
catch (DbUpdateException e) when (e.IsUniqueViolation())
{
    return;                             // already handled
}
await HandleAsync(msg);
```

### Broker Mechanics: Kafka, RabbitMQ, SQS

<p class="src">From <em>.NET Backend Deep Dive</em> · Broker Mechanics: Kafka, RabbitMQ, SQS</p>

<a id="dn-136"></a>

Part 4 covered the queue-vs-bus distinction. The brokers themselves behave very differently, and the differences drive design.

| | **Kafka** | **RabbitMQ** | **SQS** |
|---|---|---|---|
| Model | Distributed **log** — messages persist and are re-readable | Broker with exchanges and queues; messages are removed on ack | Managed queue; messages hidden then deleted |
| Ordering | Per **partition** | Per queue (broken by multiple consumers) | FIFO queues only, per **message group** |
| Retention | Time/size based — **replay is a feature** | Until acked | Up to 14 days, then dropped |
| Consumers | Consumer **groups**, one partition per member | Competing consumers off one queue | Competing receivers |
| Throughput | Very high (sequential disk, batching) | High | High, fully managed |
| Routing | Topic + partition key only | Rich: direct, topic, fanout, headers | None — one queue, one destination |
| Best at | Event streaming, replay, high volume, analytics | Complex routing, per-message workflows, RPC-ish | Simple durable decoupling on AWS |

<a id="dn-137"></a>

#### Kafka: the parts that bite

**Partitions are the unit of parallelism and of ordering.** Ordering holds *within* a partition only, so your **partition key** decides both:

```csharp
// Same key -> same partition -> ordered relative to each other
await producer.ProduceAsync("orders", new Message<string, string>
{
    Key   = order.CustomerId.ToString(),   // all events for a customer stay ordered
    Value = JsonSerializer.Serialize(evt)
});
```

Choose the key badly and you get either lost ordering (random keys) or a **hot partition** (a key like `"US"` sending 80% of traffic to one broker).

**Consumer group members can't exceed partition count.** Ten consumers on a six-partition topic means four sit idle. Partitions can be added but not removed, and adding them changes key→partition mapping, which breaks ordering for in-flight keys.

**Rebalancing** happens whenever a member joins, leaves, or fails a heartbeat. By default all consumers stop during it ("stop-the-world" rebalance). Long processing that blocks the poll loop triggers `max.poll.interval.ms`, the broker assumes you're dead, rebalances, and your work gets reprocessed by someone else — a very common production spiral. Fix by keeping per-message work short, tuning `max.poll.records`, or using cooperative-sticky assignment.

**Offsets are a commitment, not a completion:**

```
auto-commit           -> commits on a timer -> at-most-once risk (message lost on crash)
commit after handling -> at-least-once      -> the correct default, requires idempotency
```

"Exactly-once" in Kafka means the transactional producer plus `read_process_write` within Kafka only — it does **not** extend to your database. Across systems, at-least-once plus idempotency remains the answer.

<a id="dn-138"></a>

#### RabbitMQ: the parts that bite

- **Prefetch (`BasicQos`) defaults to unlimited** — one consumer grabs the whole queue into memory while the others idle. Set it to a small number (1–20 depending on processing time).
- **Ack semantics**: `autoAck: true` loses messages on crash. Ack after processing, and `nack` with `requeue: false` to route to a DLQ rather than looping forever.
- Dead-lettering is configured with `x-dead-letter-exchange`; combined with a TTL it also gives you **delayed retry** (message expires from a delay queue back into the work queue).
- **Quorum queues** for durability over the older mirrored queues.
- A queue that grows unboundedly will eventually hit the memory watermark and **block all publishers** — set a max length policy.

<a id="dn-139"></a>

#### SQS: the parts that bite

- **Visibility timeout** is the whole model: a received message is hidden for N seconds. If you don't delete it in time, it reappears and gets processed twice. Long jobs must extend the timeout (`ChangeMessageVisibility`) as they progress.
- **Standard queues are not ordered and explicitly allow duplicates.** FIFO queues give ordering per message group and 300 TPS (3,000 with batching).
- Always use **long polling** (`WaitTimeSeconds: 20`) — short polling costs more and returns empty responses.
- Configure a **redrive policy** to a DLQ with `maxReceiveCount`, or a poison message loops forever at your expense.

##### Interview answer
> "Kafka is a replayable log where partitions give you both ordering and parallelism, so the partition key is the key design decision and rebalancing is the main operational hazard. RabbitMQ is a router with rich exchange types where prefetch and ack mode are what usually go wrong. SQS is the simplest, and its visibility timeout is the thing to understand — long-running work must extend it or you get duplicate processing. In all three, delivery is at-least-once, so consumers are idempotent."

---

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a136"></a>

#### Messaging fundamentals: queues, topics and delivery

Asynchronous messaging decouples services in time. The producer does not wait, does not need the consumer up, and gets buffering and retry for free. What it costs is ordering guarantees, exactly-once semantics, and easy debugging.

``` text
  QUEUE (point to point)              TOPIC / PUB-SUB (fan out)

  producer -> [ queue ] -> consumer   publisher -> [ topic ] -> sub A -> consumer A
                        -> consumer                          -> sub B -> consumer B
  each message handled ONCE           every subscriber gets its own copy
  competing consumers = scale out     add consumers without touching the publisher
```

| Guarantee     | Reality                                                                                       |
|---------------|-----------------------------------------------------------------------------------------------|
| At most once  | Fire and forget; messages can be lost                                                         |
| At least once | What real brokers give you; duplicates happen                                                 |
| Exactly once  | Not achievable end to end — simulate it with at-least-once delivery plus idempotent consumers |

Therefore: **every consumer must be idempotent.** Give each message a stable ID, record processed IDs, and make handling a repeat a no-op. This is not optional hardening; it is the basic contract.

###### Worked example

Message ordering is the guarantee people assume they have and almost never do. Here is exactly where it breaks and what to do instead.

``` text
  producer sends, in order:   OrderPlaced(4711)  ->  OrderCancelled(4711)

  with competing consumers on one queue:

    consumer A picks up OrderPlaced     (takes 800 ms - it calls an external API)
    consumer B picks up OrderCancelled  (takes 20 ms)
    consumer B finishes FIRST -> the cancellation is processed against an order
                                 that does not exist yet -> error, or worse, ignored
    consumer A finishes -> the order now exists, and is NOT cancelled.

  scaling out consumers destroys ordering. That is the trade you made for throughput.
```

| Mechanism                                | Guarantees order within                  | Cost                                                       |
|------------------------------------------|------------------------------------------|------------------------------------------------------------|
| Single consumer, single queue            | The whole queue                          | No horizontal scaling at all                               |
| Kafka partition key                      | One partition (all messages for a key)   | Partition count caps your consumer count                   |
| Service Bus sessions                     | One session id                           | Session-enabled queues; one consumer per session at a time |
| RabbitMQ consistent-hash exchange        | One routing key                          | A plugin, and rebalancing is manual                        |
| Version / sequence number on the message | Nothing — but you can detect and reorder | Consumer complexity                                        |

``` csharp
// the technique that works regardless of broker: make handlers order-independent
public async Task Consume(ConsumeContext<OrderStatusChanged> ctx)
{
    var m = ctx.Message;

    // conditional update: only apply if this message is NEWER than what we have
    var affected = await _db.Orders
        .Where(o => o.Id == m.OrderId && o.Version < m.Version)
        .ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status,  m.Status)
            .SetProperty(o => o.Version, m.Version), ctx.CancellationToken);

    if (affected == 0)
        _log.LogDebug("Ignoring stale or duplicate {Event} v{Version} for {OrderId}",
                      nameof(OrderStatusChanged), m.Version, m.OrderId);
}
```

This single pattern — a monotonically increasing version on the aggregate, and a conditional update that discards anything older — makes a consumer simultaneously idempotent and order-independent. It is substantially less work than partitioning by key, and it survives the case where you later need to increase the consumer count.

###### Poison messages and the dead-letter workflow

``` text
  message fails
    -> immediate retry x3        (transient: a blip, a deadlock, a timeout)
    -> delay 10 s, retry x3      (short outage: a dependency restarting)
    -> delay 5 min, retry x3     (longer outage)
    -> DEAD LETTER               (it is never going to work: bad data, a bug)

  the dead-letter queue is not a graveyard. It needs:
    - an alert when it is non-empty (not a dashboard nobody looks at)
    - the original message PLUS the exception, the stack, and the attempt history
    - a one-click replay after the fix is deployed
    - a retention policy, so it does not silently discard evidence after 7 days
```

``` csharp
cfg.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1),
                                          TimeSpan.FromSeconds(30),
                                          TimeSpan.FromSeconds(2)));
cfg.UseScheduledRedelivery(r => r.Intervals(TimeSpan.FromMinutes(5),
                                            TimeSpan.FromMinutes(30),
                                            TimeSpan.FromHours(2)));
// distinguish what is worth retrying at all
cfg.UseMessageRetry(r => r.Ignore<ValidationException>()      // never going to succeed
                          .Handle<HttpRequestException>()     // probably transient
                          .Handle<DbUpdateConcurrencyException>());
```

> **\[Trap\]**
>
> **Common trap.** Retrying a non-transient failure is not free: three retries times three redeliveries on a message that can never succeed is nine executions of your handler, nine log entries, and nine calls to whatever downstream service it touches before giving up. Classify exceptions explicitly — a validation failure should dead-letter on the first attempt.

<a id="be-a137"></a>

#### Brokers: RabbitMQ, Azure Service Bus, Kafka

|            | RabbitMQ                         | Azure Service Bus                                    | Kafka                                 |
|------------|----------------------------------|------------------------------------------------------|---------------------------------------|
| Model      | Exchanges → queues, routing keys | Queues + topics with subscriptions                   | Partitioned append-only log           |
| Retention  | Until consumed                   | Until consumed / TTL                                 | Time or size based; replayable        |
| Ordering   | Per queue                        | Per session                                          | Per partition                         |
| Throughput | High                             | High                                                 | Very high                             |
| Best for   | Task queues, routing, RPC        | Enterprise messaging on Azure, sessions, dead-letter | Event streaming, audit log, analytics |

The mental model difference matters: a queue is a to-do list that empties, a Kafka topic is a durable log that many independent consumers read at their own offsets. If a new service needs the last six months of events, that is Kafka. If a worker needs to process jobs once, that is a queue.

##### Dead-letter queues

Every consumer needs a poison-message path. After N failed attempts the broker moves the message to a dead-letter queue where it can be inspected and replayed. Without one, a single malformed message blocks the queue forever or spins in an infinite retry loop burning money.

###### Choosing, and the operational shape of each

|                                       | RabbitMQ                                 | Azure Service Bus                   | Kafka                                                  |
|---------------------------------------|------------------------------------------|-------------------------------------|--------------------------------------------------------|
| Model                                 | Queue, with rich routing                 | Queue + topic                       | **Append-only log**                                    |
| Message removed on ack                | Yes                                      | Yes                                 | **No** — retained, consumers track offsets             |
| Replay                                | No                                       | No (deferred/DLQ only)              | **Native**                                             |
| Ordering                              | Per queue                                | Per session                         | Per **partition**                                      |
| Routing                               | Excellent — exchanges, bindings, headers | Good — subscriptions with filters   | Minimal — topic + partition key                        |
| Throughput                            | High                                     | High                                | **Very high**                                          |
| Per-message TTL, priority, scheduling | Yes                                      | Yes                                 | No                                                     |
| Operational burden                    | Moderate                                 | **Managed**                         | **High** if self-hosted                                |
| Fits                                  | Work distribution, complex routing       | Azure estates, enterprise messaging | Event streaming, several independent consumers, replay |

``` text
  the distinction that decides it

  QUEUE       producer -> [ msg msg msg ] -> consumer
                                             REMOVED once acknowledged
              - competing consumers share the work
              - the broker tracks per-message state
              - once consumed, it is gone

  LOG         producer -> [0 1 2 3 4 5 6 ...] -> group A at offset 6
                           RETAINED             -> group B at offset 2
              - consumers track their OWN position
              - messages stay for the retention period regardless
              - a NEW consumer can read from the beginning

  the question that separates them:
    "will another team, later, need to consume this same stream
     and see what already happened?"
      yes -> log.   no -> queue.
```

A note on scope: for moderate throughput, a database table with `FOR UPDATE SKIP LOCKED` is a genuinely good queue, is transactional with your data, and adds no infrastructure. Reach for a broker when you need cross-service fan-out, very high throughput, or retention and replay — not as the default for “we need background work”.

| Kafka concept          | Operational consequence                                                                      |
|------------------------|----------------------------------------------------------------------------------------------|
| **Partition**          | Consumer parallelism is capped at partition count. You can add partitions, never remove them |
| **Consumer lag**       | **The metric that matters.** Rising lag means you are falling behind, permanently            |
| **Rebalance**          | A stop-the-world pause when membership changes. Frequent rebalances kill throughput          |
| `max.poll.interval.ms` | Exceed it by processing slowly and you are ejected mid-batch → duplicates                    |
| **Retention**          | Storage sizing follows directly. A topic with no retention policy fills a disk               |
| **Compaction**         | Keep only the latest value per key — for changelog and state topics                          |

<a id="be-a138"></a>

#### MassTransit, Wolverine and the abstraction layer

Talking to a broker's raw client means writing serialization, retries, dead-lettering, correlation and topology by hand. A messaging framework gives you all of it plus a broker-agnostic API.

``` csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();
    x.SetKebabCaseEndpointNameFormatter();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(cfg2 => { });
        cfg.UseMessageRetry(r => r.Exponential(5,
            TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(ctx);
    });
});

public class OrderPlacedConsumer(IInventory inv) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> ctx)
        => await inv.ReserveAsync(ctx.Message.OrderId, ctx.Message.Lines, ctx.CancellationToken);
}
```

> **\[Note\]**
>
> **Message design.** Publish *events* in the past tense describing what happened (`OrderPlaced`) and send *commands* in the imperative to one known handler (`ReserveStock`). Events have many subscribers and the publisher must not care who; commands have exactly one owner.

###### What an abstraction buys, and what it hides

**What you get**

- One programming model over several brokers
- Retry, redelivery and dead-lettering as policy
- Outbox and inbox implementations, tested
- Saga state machines with persistence
- Serialisation, headers and correlation handled
- Test harnesses that do not need a broker

**What it hides**

- Broker-specific semantics that still apply
- Where the message actually is when something fails
- The real cost of a retry policy you configured casually
- Which failures are transient and which are poison
- Version coupling — the library dictates your upgrade path
- Licensing: MassTransit v9 moved to a commercial model

``` csharp
// MassTransit: consumer + retry + outbox
public class OrderPlacedConsumer(IOrderService svc) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> ctx)
    {
        // ctx.Message is deserialised; correlation and headers are handled.
        await svc.HandleAsync(ctx.Message, ctx.CancellationToken);
    }
}

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();

    // the transactional outbox - the single most valuable feature here
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();          // publishes only after SaveChanges commits
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(cs);
        cfg.UseMessageRetry(r => r.Exponential(3,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(ctx);
    });
});
```

``` text
  RETRY, REDELIVERY AND DEAD-LETTERING - three different things

  RETRY          in-process, immediately, holding the message.
                 for a transient blip: a deadlock, a timeout.
                 -> keep it SHORT. A 30-second retry policy holds a
                    consumer slot for 30 seconds doing nothing.

  REDELIVERY     put it back on the broker with a delay, release the
                 consumer, pick it up later.
                 for a dependency that is DOWN and will return.
                 -> minutes to hours. Does not block other messages.

  DEAD LETTER    after N attempts, move it aside.
                 for a POISON message that will never succeed.
                 -> and this is the one teams forget to monitor.

  the common misconfiguration: an aggressive in-process retry policy for
  a failure that needs redelivery. Every consumer sits retrying against
  a dead dependency, the queue backs up, and adding consumers does not
  help because they all block the same way.
```

> **\[Note\]**
>
> **The abstraction is worth it for the outbox alone.** Getting exactly the transactional outbox right — message written in the same transaction as the data, a relay that is idempotent, deduplication on the consumer side, and correct behaviour when the relay crashes mid-publish — is a genuinely fiddly piece of infrastructure. If you use nothing else from MassTransit or Wolverine, that component justifies the dependency.

<a id="be-a139"></a>

#### The outbox pattern and idempotency

The single most common distributed-systems bug in a .NET codebase: save to the database, then publish a message. If the process dies between the two, the state changed and nobody was told. Publish first and the opposite happens. There is no ordering that fixes it, because two systems cannot commit atomically.

``` text
  WRITE PATH (one local transaction)
    BEGIN
      UPDATE orders SET status='Placed'
      INSERT INTO outbox (id, type, payload, occurred_at)
    COMMIT

  RELAY (separate background service)
    poll outbox WHERE published_at IS NULL
      -> publish to broker  -> mark published
      -> crash before mark? -> republished later (duplicate, safe by design)
```

``` csharp
// consumer side: dedupe on the message id
public async Task Consume(ConsumeContext<OrderPlaced> ctx)
{
    var msgId = ctx.MessageId!.Value;
    if (await _db.Processed.AnyAsync(p => p.Id == msgId)) return;   // already done

    await _handler.HandleAsync(ctx.Message);
    _db.Processed.Add(new ProcessedMessage(msgId, DateTimeOffset.UtcNow));
    await _db.SaveChangesAsync();     // same transaction as the side effect
}
```

The mirror image is the *inbox* pattern, and together they give you effectively-once processing on top of at-least-once delivery. MassTransit and Wolverine both ship transactional outbox support — use it rather than writing your own relay.

###### Worked example

A complete, working outbox. This is short enough to own rather than take a dependency on, and seeing the whole thing makes the guarantees obvious.

``` csharp
// 1. the table
public class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();   // time-ordered, index-friendly
    public string Type { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}

b.Entity<OutboxMessage>()
 .HasIndex(m => new { m.ProcessedAt, m.OccurredAt })
 .HasFilter("processed_at IS NULL");        // a filtered index: the poll query stays tiny forever
```

``` csharp
// 2. the write - the event goes in the SAME transaction as the state change
public async Task<Guid> Handle(PlaceOrder cmd, CancellationToken ct)
{
    var order = Order.Place(cmd.CustomerId, cmd.Lines);
    _db.Orders.Add(order);

    _db.Outbox.Add(new OutboxMessage
    {
        Type = nameof(OrderPlaced),
        Payload = JsonSerializer.Serialize(new OrderPlaced(order.Id, order.Total)),
        OccurredAt = _time.GetUtcNow()
    });

    await _db.SaveChangesAsync(ct);     // ONE transaction: either both, or neither
    return order.Id;
}
```

``` csharp
// 3. the relay
protected override async Task ExecuteAsync(CancellationToken ct)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
    while (await timer.WaitForNextTickAsync(ct))
    {
        using var scope = _scopes.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDb>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        // FOR UPDATE SKIP LOCKED lets N replicas poll safely without a leader election
        var batch = await db.Outbox
            .FromSql($@"SELECT * FROM outbox WHERE processed_at IS NULL
                        ORDER BY occurred_at LIMIT 100 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        foreach (var m in batch)
        {
            try
            {
                await bus.PublishAsync(m.Type, m.Payload, messageId: m.Id, ct);
                m.ProcessedAt = _time.GetUtcNow();
            }
            catch (Exception ex)
            {
                m.Attempts++; m.Error = ex.Message;
                if (m.Attempts >= 10) m.ProcessedAt = _time.GetUtcNow();   // give up -> alert
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
```

``` text
  guarantees this gives you

  crash after COMMIT, before publish   -> relay finds the row later and publishes.  OK
  crash after publish, before mark     -> relay republishes. DUPLICATE.
                                          -> which is why the consumer dedupes on m.Id
  broker down for an hour              -> rows accumulate, publish when it returns. OK
  two relay replicas running           -> SKIP LOCKED means they take different rows. OK

  what it does NOT give you: ordering across different aggregates, or low latency.
  a 2-second poll means up to 2 seconds of delay. Use LISTEN/NOTIFY, CDC, or a
  push from the write path as an optimisation on top - never as the only mechanism.
```

###### Idempotency on the consumer side, including the HTTP case

``` csharp
// message consumers: dedupe table in the same transaction as the side effect
public async Task Consume(ConsumeContext<OrderPlaced> ctx)
{
    var id = ctx.MessageId!.Value;

    await using var tx = await _db.Database.BeginTransactionAsync(ctx.CancellationToken);

    _db.ProcessedMessages.Add(new ProcessedMessage(id, _time.GetUtcNow()));
    try { await _db.SaveChangesAsync(ctx.CancellationToken); }
    catch (DbUpdateException) when (IsUniqueViolation) { return; }   // already handled

    await _handler.HandleAsync(ctx.Message, ctx.CancellationToken);
    await _db.SaveChangesAsync(ctx.CancellationToken);
    await tx.CommitAsync(ctx.CancellationToken);
}
```

``` csharp
// HTTP APIs: the Idempotency-Key header, which every payment API implements
app.MapPost("/payments", async (HttpContext ctx, ChargeRequest req, IPayments p, AppDb db) =>
{
    if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var key))
        return Results.BadRequest("Idempotency-Key header is required.");

    var existing = await db.IdempotencyRecords.FindAsync([key.ToString()]);
    if (existing is not null)
        return existing.RequestHash == Hash(req)
            ? Results.Content(existing.ResponseBody, "application/json", statusCode: existing.Status)
            : Results.Conflict("This key was used with a different request body.");

    var result = await p.ChargeAsync(req);
    db.IdempotencyRecords.Add(new(key!, Hash(req), 201, JsonSerializer.Serialize(result)));
    await db.SaveChangesAsync();
    return Results.Created($"/payments/{result.Id}", result);
});
```

The request-hash check is the part people omit and then regret: without it, a client that reuses a key for a different payment silently receives the first payment's response and believes the second one succeeded. Returning 409 makes the client bug visible instead of turning it into a financial discrepancy.

<a id="be-a140"></a>

#### Sagas and long-running processes

A business transaction spanning several services cannot use a database transaction. A saga breaks it into local transactions, each with a compensating action if a later step fails.

``` text
  Place order saga

  1. OrderPlaced        -> reserve stock        (compensate: release stock)
  2. StockReserved      -> take payment         (compensate: refund)
  3. PaymentTaken       -> book shipment        (compensate: cancel shipment)
  4. ShipmentBooked     -> OrderConfirmed

  step 3 fails -> run compensations for 2 then 1 -> OrderCancelled
```

**Choreography**

- Each service reacts to events, no coordinator
- Loosely coupled, easy to add a step
- The overall flow exists nowhere in code
- Hard to answer ‘where is order 4711 stuck?’

**Orchestration**

- A saga state machine drives every step
- Flow is explicit, testable and queryable
- The orchestrator is a coupling point
- Better for anything with more than three steps

Compensations are not rollbacks — they are new business facts. You do not un-charge a card, you issue a refund, and both appear in the ledger. Design them as first-class domain operations.

###### Orchestration vs choreography, and compensation that actually works

``` text
  ORCHESTRATION                       CHOREOGRAPHY

    [ Saga ]                          OrderPlaced
     |  |  |                              |
     v  v  v                          PaymentSvc reacts -> PaymentTaken
   Pay Ship Notify                                             |
                                      ShippingSvc reacts -> Shipped
  one component knows the whole                                |
  process and issues commands       InventorySvc reacts -> Reserved

  + the flow is in ONE place,        + no central component
    readable and testable            + services are decoupled
  + easy to see where it is stuck    - NOBODY knows the whole flow
  + easy to add a step               - debugging means correlating logs
  - the orchestrator is coupled        across N services
    to every participant             - adding a step means finding every
                                       subscriber that must change

  for anything with more than about three steps, ORCHESTRATION.
  choreography is elegant on a whiteboard and hard to operate at 3am.
```

``` csharp
// MassTransit state machine - the saga as explicit state
public class OrderSaga : MassTransitStateMachine<OrderState>
{
    public State AwaitingPayment { get; } = null!;
    public State AwaitingShipment { get; } = null!;

    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);

        Initially(
            When(OrderSubmitted)
                .Then(c => c.Saga.Total = c.Message.Total)
                .Send(ctx => new TakePayment(ctx.Saga.CorrelationId, ctx.Saga.Total))
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            When(PaymentTaken)
                .Send(ctx => new ShipOrder(ctx.Saga.CorrelationId))
                .TransitionTo(AwaitingShipment),
            When(PaymentFailed)
                .Send(ctx => new CancelOrder(ctx.Saga.CorrelationId))
                .Finalize(),
            // ALWAYS have a timeout. A saga with no timeout waits forever
            // for a message that a crashed service will never send.
            When(PaymentTimeout.Received)
                .Send(ctx => new CancelOrder(ctx.Saga.CorrelationId))
                .Finalize());

        During(AwaitingShipment,
            When(ShipmentFailed)
                .Send(ctx => new RefundPayment(ctx.Saga.CorrelationId,   // COMPENSATE
                                               ctx.Saga.Total))
                .Finalize());
    }
}
```

| Compensation reality              | Detail                                                                            |
|-----------------------------------|-----------------------------------------------------------------------------------|
| It is **not** a rollback          | The payment happened. A refund is a *new* business event, visible to the customer |
| Compensations can fail too        | Then you need a manual queue and a human. Build it before you need it             |
| Order matters                     | Compensate in reverse order of the steps that succeeded                           |
| Some steps cannot compensate      | An email was sent. Design so irreversible steps come **last**                     |
| Compensation must be idempotent   | It will be retried, like everything else                                          |
| The saga itself needs persistence | In-memory state means a restart loses every in-flight process                     |

> **\[Trap\]**
>
> **Common trap.** Every saga needs a timeout on every waiting state, and an operational view of stuck instances. Without both, a service that crashes mid-flow leaves sagas waiting forever for a message nobody will send — and because nothing errors, nothing alerts. The first you hear of it is a customer asking why their order from three weeks ago never shipped.

<a id="be-a148"></a>

#### Durable workflow orchestration

Sagas solve distributed consistency; they do not solve *durable execution*. A process that must wait three days for an approval, survive a deployment mid-flight, retry step four without repeating steps one to three, and be queryable by support, is a different problem — and hand-rolling it produces a state machine plus a scheduler plus a persistence layer plus a retry engine, badly.

| Engine                              | Model                                                          | Runs on                       | Fits                                                           |
|-------------------------------------|----------------------------------------------------------------|-------------------------------|----------------------------------------------------------------|
| **Temporal**                        | Code-as-workflow; the runtime replays history to rebuild state | Self-hosted or Temporal Cloud | Complex, long-running business processes; strongest guarantees |
| **Azure Durable Functions**         | Orchestrator functions; event-sourced state                    | Azure Functions               | Azure shops; serverless, low ceremony                          |
| **Dapr Workflows**                  | Similar model, sidecar-hosted                                  | Anywhere Dapr runs            | Already using Dapr; portable                                   |
| **Elsa Workflows**                  | Code or visual designer, .NET-native                           | In your own host              | Human-in-the-loop, business-user-visible flows                 |
| **MassTransit / NServiceBus sagas** | State machine driven by messages                               | Your broker                   | Message-centric processes; you already have the bus            |

``` csharp
// Temporal: this reads like ordinary sequential code, and survives process restarts
public class OrderFulfilmentWorkflow : IOrderFulfilmentWorkflow
{
    private readonly ActivityOptions _opts = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new() { MaximumAttempts = 5, BackoffCoefficient = 2 }
    };

    private bool _approved;

    [WorkflowRun]
    public async Task<FulfilmentResult> RunAsync(OrderPlaced input)
    {
        // each Activity is retried independently, and its result is recorded in history
        var reservation = await Workflow.ExecuteActivityAsync(
            (IInventoryActivities a) => a.ReserveAsync(input.OrderId, input.Lines), _opts);

        try
        {
            var payment = await Workflow.ExecuteActivityAsync(
                (IPaymentActivities a) => a.ChargeAsync(input.OrderId, input.Total), _opts);

            if (input.Total > 10_000m)
            {
                // waits for a SIGNAL. For three days if necessary. Across deployments.
                var got = await Workflow.WaitConditionAsync(() => _approved,
                                                            TimeSpan.FromDays(3));
                if (!got) throw new ApplicationFailureException("Approval timed out.");
            }

            await Workflow.ExecuteActivityAsync(
                (IShippingActivities a) => a.DispatchAsync(input.OrderId), _opts);

            return FulfilmentResult.Success();
        }
        catch (ActivityFailureException)
        {
            // compensation is just... code. In the same place as the happy path.
            await Workflow.ExecuteActivityAsync(
                (IInventoryActivities a) => a.ReleaseAsync(reservation.Id), _opts);
            throw;
        }
    }

    [WorkflowSignal] public Task ApproveAsync() { _approved = true; return Task.CompletedTask; }
    [WorkflowQuery]  public string Status => _approved ? "approved" : "awaiting-approval";
}
```

###### How durable execution works, and the constraints it imposes

``` text
  the workflow code is a PURE FUNCTION of its event history

  history:  WorkflowStarted
            ActivityScheduled(Reserve)   ActivityCompleted(Reserve, {id: 88})
            ActivityScheduled(Charge)    ActivityCompleted(Charge, {ref: "px_1"})
            TimerStarted(3d)

  process crashes / is redeployed
    -> a new worker picks it up
    -> it RE-EXECUTES the workflow code from line 1
    -> each Workflow.ExecuteActivityAsync returns the RECORDED result instead of re-running
    -> execution fast-forwards to exactly where it was, with all local variables restored
    -> and then continues

  which is why the code must be DETERMINISTIC.
```

| Forbidden in workflow code                      | Use instead                                 |
|-------------------------------------------------|---------------------------------------------|
| `DateTime.UtcNow`                               | `Workflow.UtcNow`                           |
| `Guid.NewGuid()`                                | `Workflow.NewGuid()`                        |
| `Random`                                        | `Workflow.Random`                           |
| `Task.Delay`                                    | `Workflow.DelayAsync`                       |
| Direct HTTP or database calls                   | An Activity — that is what they are for     |
| `Task.Run`, threads, locks                      | The workflow runs single-threaded by design |
| Iterating a `Dictionary` and depending on order | An ordered collection                       |

The same rule creates the versioning problem: changing workflow code changes the function, so a workflow started under v1 may not replay correctly against v2. Engines provide patching APIs (`Workflow.Patched("add-approval-step")`) so old executions take the old branch and new ones take the new. This is the main ongoing cost of the pattern, and it is the same shape as event schema versioning.

**Worth an orchestration engine when**

- Processes run for hours, days or weeks
- Human approval steps are involved
- Compensation logic is non-trivial
- Support must query ‘where is order 4711 stuck?’
- You are already maintaining a home-grown version of this

**Overkill when**

- Everything completes in seconds
- Three steps with no compensation
- A broker with retries and a dead-letter queue suffices
- Nobody will operate another platform component

<a id="be-a150"></a>

#### Change data capture as an integration mechanism

Reading the database's transaction log turns every committed write into an event stream, with no application changes at all. It is the pragmatic way to get events out of a system you cannot modify — which describes most legacy platforms.

``` text
  application  ->  database  ->  WAL / transaction log
                                     |
                              [ Debezium / native CDC ]
                                     |
                                 Kafka topic per table
                                     |
                       +-------------+-------------+
                       v             v             v
                  search index   read model    other service

  the application knows nothing about any of this.
```

**CDC**

- Zero application changes — works on legacy
- Cannot miss a write; the log is the source
- Captures before and after images
- **Exposes your schema as the contract**
- Events are row-shaped, not business-shaped

**Outbox**

- Events are business-shaped and intentional
- The schema stays private
- Requires modifying the application
- You control the payload and its versioning
- The right choice for anything you own

``` json
{
  "name": "legacy-orders-connector",
  "config": {
    "connector.class": "io.debezium.connector.sqlserver.SqlServerConnector",
    "database.hostname": "sql.internal",
    "database.names": "LegacyDb",
    "table.include.list": "dbo.Orders,dbo.OrderLines",
    "topic.prefix": "legacy",
    "snapshot.mode": "initial",
    "transforms": "unwrap",
    "transforms.unwrap.type": "io.debezium.transforms.ExtractNewRecordState",
    "transforms.unwrap.delete.handling.mode": "rewrite"
  }
}
```

``` csharp
// consume it, and TRANSLATE immediately - this is an anti-corruption layer
public sealed class OrderRowConsumer(IOrderProjection projection) : IConsumer<OrderRowChanged>
{
    public async Task Consume(ConsumeContext<OrderRowChanged> ctx)
    {
        var row = ctx.Message;

        // the row shape is the legacy schema. It must not escape this class.
        var evt = row.Operation switch
        {
            "c" => new OrderAppeared(row.After!.Id, Map(row.After)),
            "u" when row.Before!.StatusCode != row.After!.StatusCode
                => new OrderStatusChanged(row.After.Id, MapStatus(row.After.StatusCode)),
            "u" => new OrderUpdated(row.After!.Id, Map(row.After)),
            "d" => new OrderRemoved(row.Before!.Id),
            _   => null
        };

        if (evt is not null) await projection.ApplyAsync(evt, ctx.CancellationToken);
    }
}
```

###### Practical constraints before you commit to it

| Constraint                       | Detail                                                                                                          |
|----------------------------------|-----------------------------------------------------------------------------------------------------------------|
| Schema is the contract           | A column rename in the source breaks every consumer. Mitigate with an ACL at the consumer and a schema registry |
| No business intent               | You get ‘`status` changed from 2 to 3’, not ‘order shipped’. The meaning must be reconstructed                  |
| Bulk operations flood the stream | A one-line `UPDATE` touching 5 million rows emits 5 million events                                              |
| Initial snapshot                 | The first run reads the whole table. On a large table this needs planning and possibly a read replica           |
| Log retention                    | If the connector is down longer than the log is retained, you need a fresh snapshot                             |
| Permissions and load             | CDC must be enabled on the source, and it adds write overhead — get DBA agreement, not just access              |
| Soft deletes                     | A soft delete is an update, not a delete. Consumers must know the convention                                    |

``` csharp
// RavenDB subscription: CDC without the infrastructure
var name = await store.Subscriptions.CreateAsync<Order>(x => x.Status != null);

using var worker = store.Subscriptions.GetSubscriptionWorker<Order>(
    new SubscriptionWorkerOptions(name) { MaxDocsPerBatch = 500 });

await worker.Run(async batch =>
{
    foreach (var item in batch.Items)
        await projection.ApplyAsync(Translate(item.Result), ct);
}, ct);
// the server tracks the acknowledged position, so a restart resumes exactly where it stopped
```

### Brokers & Queues

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Brokers & Queues</p>

<p class="pd">Queues and logs as infrastructure you operate rather than libraries you call: what they guarantee, how they are sized, and how they fail.</p>

<a id="be-g692"></a>

#### Queues and logs are different things

``` text
  QUEUE (RabbitMQ, SQS, Azure Service Bus)

    producer -> [ msg msg msg ] -> consumer
                                   message is REMOVED once acknowledged
    - competing consumers share the work
    - per-message acknowledgement, redelivery, dead-lettering
    - the broker tracks per-message state
    - once consumed, it is gone. No replay.

  LOG (Kafka, Kinesis, Pulsar, Redpanda)

    producer -> [ 0 1 2 3 4 5 6 7 8 ... ] -> consumer group A (at offset 6)
                 an APPEND-ONLY, RETAINED sequence
                                          -> consumer group B (at offset 2)
    - consumers track their own OFFSET
    - messages stay for the retention period regardless of consumption
    - REPLAY is native: reset the offset and reprocess
    - ordering guaranteed WITHIN A PARTITION only
```

| Need                                                      | Choose                              |
|-----------------------------------------------------------|-------------------------------------|
| Distribute work across workers                            | Queue                               |
| Complex routing, priorities, per-message TTL              | Queue (RabbitMQ)                    |
| Several independent consumers of the same events          | Log                                 |
| Replay history, or add a new consumer that needs the past | Log                                 |
| Very high throughput, ordered per key                     | Log                                 |
| Simple, managed, low volume                               | SQS or Service Bus — and stop there |
| Event sourcing                                            | Log, or a purpose-built event store |

A note on scope: for moderate throughput, a database table with `FOR UPDATE SKIP LOCKED` is a perfectly good queue, is transactional with your data, and adds no infrastructure. Reach for a broker when you need cross-service fan-out, very high throughput, or retention and replay — not by default.

<a id="be-g693"></a>

#### Delivery guarantees, in practice

``` text
  AT MOST ONCE     acknowledge BEFORE processing
                   -> a crash loses the message. Fast, lossy.

  AT LEAST ONCE    acknowledge AFTER processing
                   -> a crash after processing but before ack redelivers
                   -> THE NORMAL GUARANTEE. Consumers MUST be idempotent.

  EXACTLY ONCE     not achievable end-to-end across a network.
                   what vendors provide:
                     - transactional writes WITHIN the broker
                     - deduplication over a bounded window
                   what you actually build:
                     at-least-once delivery + idempotent processing
                     = the same observable outcome, and it is honest
                       about where the work lives.
```

``` sql
-- idempotent consumption, at the database, which is where it belongs
BEGIN;
  INSERT INTO processed_messages (message_id, processed_at)
  VALUES (:message_id, now())
  ON CONFLICT (message_id) DO NOTHING;

  -- 0 rows inserted = already processed. Acknowledge and stop.
  IF NOT FOUND THEN ROLLBACK; RETURN 'duplicate'; END IF;

  -- do the actual work in the SAME transaction
  UPDATE orders SET status = 'Shipped' WHERE id = :order_id;
COMMIT;
-- now redelivery is harmless, and the guarantee does not depend on the broker.
```

| Consumer concern        | Handling                                                           |
|-------------------------|--------------------------------------------------------------------|
| Duplicate delivery      | An idempotency key, checked in the same transaction as the work    |
| Poison message          | Retry with backoff, then dead-letter after N attempts              |
| Ordering                | Partition or session by the key that needs ordering — not globally |
| Slow consumer           | Consumer lag as a first-class metric; scale consumers              |
| Backlog after an outage | Bounded concurrency, so catch-up does not overwhelm downstream     |
| Message schema change   | Version the payload; consumers tolerate unknown fields             |

> **\[Trap\]**
>
> **Common trap.** A dead-letter queue nobody monitors is a silent data-loss mechanism. Messages fail, land in the DLQ, and nobody notices until a customer asks why their order never shipped. Alert on DLQ depth greater than zero, give it an owner, and build a documented path to inspect and replay — the replay tooling is what people always skip and always need.

###### The three guarantees, and what each costs to implement

``` text
  AT-MOST-ONCE       acknowledge BEFORE processing
                       fast, and messages are lost on a crash
                       -> metrics, telemetry, cache invalidation

  AT-LEAST-ONCE      acknowledge AFTER processing
                       nothing is lost; duplicates occur on retry
                       -> THE DEFAULT, and what nearly everything uses

  EXACTLY-ONCE       not achievable in DELIVERY.
                       achievable as an EFFECT:
                         at-least-once + idempotent processing

  WHY delivery cannot be exactly-once, stated once and for all:

    the sender transmits. No acknowledgement arrives.
      retransmit -> risk delivering twice
      give up    -> risk delivering zero times
    there is no third option, and no protocol removes the choice.

  so the guarantee MOVES to the consumer, which has durable state and
  can remember what it has already done.
```

``` sql
-- the idempotent consumer. The dedup check and the work MUST share one
-- transaction, or the race has moved rather than gone.
BEGIN;
  INSERT INTO processed_messages (message_id, consumer, processed_at)
  VALUES (:msg_id, 'orders-svc', now())
  ON CONFLICT (message_id, consumer) DO NOTHING;
  -- 0 rows inserted -> already handled. ROLLBACK, ack, done.

  UPDATE orders SET status = 'Shipped' WHERE id = :order_id;
COMMIT;

-- and the table needs a retention policy, or it grows forever:
DELETE FROM processed_messages WHERE processed_at < now() - interval '30 days';
-- 30 days must EXCEED the broker's maximum redelivery window, including
-- dead-letter reprocessing. Too short, and an old redelivery is
-- processed twice.
```

| Ack timing                           | On consumer crash mid-processing              | Suits                               |
|--------------------------------------|-----------------------------------------------|-------------------------------------|
| Ack on receive                       | **Message lost**                              | Telemetry, where loss is acceptable |
| Ack after processing                 | **Redelivered**                               | Almost everything                   |
| Ack after commit, in one transaction | Redelivered, and deduplicated                 | Financial and stateful work         |
| Manual ack, long processing          | Redelivered *and* the consumer may be evicted | Needs a heartbeat or extension      |

The last row is the trap in long-running consumers. Brokers assume a consumer that has not acknowledged within a visibility or poll timeout is dead, and redeliver — while your consumer is still working. You then have two consumers processing the same message concurrently. Either extend the visibility timeout periodically while working, or move the work off the poll loop entirely and acknowledge based on durable state.

<a id="be-g694"></a>

#### Operating a broker

| Kafka concept           | Means                                         | Operational consequence                                                                          |
|-------------------------|-----------------------------------------------|--------------------------------------------------------------------------------------------------|
| **Partition**           | The unit of parallelism and ordering          | Consumer parallelism is capped at the partition count. You can add partitions, never remove them |
| **Replication factor**  | Copies of each partition                      | 3 for production. RF=1 means a broker failure loses data                                         |
| **min.insync.replicas** | How many must acknowledge a write             | With `acks=all` and `min.insync=2` on RF=3, you survive one broker loss without data loss        |
| **Consumer group**      | Consumers sharing the partitions of a topic   | Adding a consumer beyond the partition count leaves it idle                                      |
| **Consumer lag**        | Offsets behind the head                       | **The single most important metric** — growing lag means you are falling behind                  |
| **Retention**           | Time or size based                            | Storage sizing follows directly from it                                                          |
| **Compaction**          | Keep only the latest value per key            | For changelog and state topics                                                                   |
| **Rebalance**           | Partitions reassigned when membership changes | A stop-the-world pause. Frequent rebalances are a real problem                                   |

``` text
# sizing, from first principles
#   throughput      1,000 msg/s x 2 KB = 2 MB/s
#   retention       7 days = 2 MB/s x 604,800 s = ~1.2 TB
#   replication     x3 = ~3.6 TB across the cluster
#   headroom        + 40% for spikes and rebuilds -> ~5 TB
#
#   partitions      target ~10 MB/s per partition for consumers,
#                   and at least as many as your peak consumer count.
#                   too few = no parallelism. Too many = more metadata,
#                   longer rebalances, more open file handles.

# the metric that matters, watched continuously
kafka-consumer-groups --bootstrap-server broker:9092 --describe --group orders-svc
# LAG column. Rising steadily = consumers cannot keep up.
```

| Failure                     | Symptom                           | Cause                                                         |
|-----------------------------|-----------------------------------|---------------------------------------------------------------|
| Rising consumer lag         | Processing falls behind           | Slow consumer, too few partitions, or a downstream bottleneck |
| Frequent rebalances         | Throughput drops, duplicates rise | `max.poll.interval.ms` exceeded by slow processing            |
| Disk full on a broker       | Writes stop                       | Retention too long, or a topic without a retention policy     |
| Under-replicated partitions | Reduced durability                | A broker down, or replication cannot keep up                  |
| Producer timeouts           | Writes fail                       | `min.insync.replicas` unsatisfiable with a broker down        |
| One partition far larger    | Uneven load                       | A skewed partition key — one tenant dominating                |

> **\[Note\]**
>
> **Use a managed broker.** Kafka in particular is operationally demanding: broker rebalancing, partition reassignment, ZooKeeper or KRaft, disk sizing, upgrade paths. MSK, Confluent Cloud, Event Hubs and Redpanda Cloud remove nearly all of it. If your reason for self-hosting is cost, price the engineering time honestly — it is usually the larger number.

###### Consumer lag, rebalancing, and the metric that matters

``` text
  LAG is the only broker metric that reliably predicts an incident,
  and QUEUE DEPTH is the one people watch instead.

    depth  = how many messages are waiting
    lag    = how far behind the consumer is, in offsets
    AGE OF THE OLDEST MESSAGE = how stale your data is  <- the useful one

    depth of 10,000 with a consumer doing 50,000/s   -> 0.2 s behind. Fine.
    depth of 500 with a consumer doing 10/s          -> 50 s behind. Not fine.

    depth alone cannot distinguish those. Age can, and age is what a
    downstream user actually experiences.

  and the shape of the graph tells you the cause:

    lag FLAT and non-zero        steady state; consumer keeps up
    lag RISING LINEARLY          consumer is slower than the producer.
                                 permanent - it will never catch up.
    lag SAWTOOTH                 batch producer; consumer drains between
    lag SPIKING at intervals     a rebalance, or a slow downstream call
```

``` text
# Kafka: read lag per partition, not just per group
kafka-consumer-groups.sh --bootstrap-server $B --describe --group orders-svc
# TOPIC   PARTITION  CURRENT-OFFSET  LOG-END-OFFSET  LAG   CONSUMER-ID
# orders  0          1048576         1048580         4     consumer-1
# orders  1          1048201         1099998         51797 consumer-2   <-- HERE
# orders  2          1048570         1048580         10    consumer-3
#
# one partition badly behind while others are fine means:
#   a HOT KEY - one partition key dominates the traffic, OR
#   one consumer instance is unhealthy, OR
#   a poison message being retried in a loop
# aggregate group lag would have averaged this away entirely.
```

| Kafka fact                                       | Operational consequence                                                  |
|--------------------------------------------------|--------------------------------------------------------------------------|
| Parallelism is capped by partition count         | 8 partitions means at most 8 useful consumers. Adding a 9th does nothing |
| Partitions can be added, never removed           | Over-partitioning is cheaper than under-partitioning                     |
| Ordering is per partition only                   | Same key → same partition → ordered. Across keys, no guarantee           |
| A rebalance stops **all** consumers in the group | Frequent rebalances destroy throughput                                   |
| `max.poll.interval.ms` exceeded → eviction       | Slow processing triggers a rebalance *and* redelivery — duplicates       |
| Retention is time or size based                  | A topic with no policy fills a disk                                      |

The rebalance-eviction loop is worth recognising because it is self-reinforcing: processing slows, a consumer misses its poll deadline, the group rebalances, every consumer stops, the backlog grows, processing is slower still on resume. The fix is either faster processing or a larger `max.poll.interval.ms` — and decoupling the poll loop from the work, so polling continues while processing happens elsewhere.

<a id="be-g695"></a>

#### Backpressure and the shape of a pipeline

``` text
  what happens without backpressure

  producer 5,000 msg/s  ->  queue  ->  consumer 1,000 msg/s

    the queue grows by 4,000/s. Nothing errors. Everything looks healthy.
    an hour later there are 14 million messages, the broker's disk is
    filling, and latency from produce to consume is over an hour -
    so the data your consumers act on is stale by an hour.

  and the recovery problem: even if production stops entirely, working
  through the backlog at 1,000/s takes four hours.

  the controls, in order of preference
    1  make the consumer faster / scale it out (partitions permitting)
    2  BOUND the queue and reject or slow producers when it is full
    3  shed load at the producer - drop what is least valuable
    4  alert on QUEUE DEPTH and AGE, not just on errors
```

| Metric                         | Tells you                                              |
|--------------------------------|--------------------------------------------------------|
| Queue depth                    | How far behind you are, in messages                    |
| **Message age at consumption** | How far behind you are, *in time* — the one users feel |
| Consumer lag                   | The same, for a log                                    |
| Production vs consumption rate | Whether the gap is growing or closing                  |
| DLQ depth                      | How much is failing permanently                        |
| Processing duration p99        | Whether consumers are degrading                        |

The metric most often missing is **age at consumption**. Depth alone is ambiguous — ten thousand messages is fine at 50,000/s and a crisis at 10/s. Age answers the question the business actually asks, which is how stale the data is.

> **\[Note\]**
>
> **Design the backlog case deliberately.** Ask what happens if consumers stop for an hour. How large does the queue get, does it hit a limit, how long does catch-up take, and does catch-up at full speed overwhelm the database? A pipeline that works in steady state and collapses during recovery is the common shape, and it fails at exactly the moment you most need it to work.

### LINQ, messaging and caching

<p class="src">From <em>Backend Engineering</em> · Practical Answers · LINQ, messaging and caching</p>

<p class="pd">Aggregating data without a second query, the difference between a queue and a bus, and the cache stack from in-process to distributed.</p>

<a id="be-q14"></a>

#### Queue versus bus: two different shapes

|                   | Queue (point-to-point)            | Bus (publish/subscribe)                           |
|-------------------|-----------------------------------|---------------------------------------------------|
| Delivery          | One consumer gets each message    | Every subscriber gets a copy                      |
| Coupling          | Sender knows the destination      | Publisher knows nothing about subscribers         |
| Message           | A **command**: `SendEmail`        | An **event**: `OrderPlaced`                       |
| Adding a consumer | Splits the load                   | Adds a new reaction, no producer change           |
| Typical use       | Work distribution, job processing | Integration between services                      |
| In practice       | RabbitMQ queue, SQS, Azure Queue  | RabbitMQ exchange, Kafka topic, Service Bus topic |

``` csharp
// COMMAND → a specific handler is expected to do this
await _bus.Send(new ChargeCard(orderId, amount));      // one consumer

// EVENT → this happened; whoever cares, react
await _bus.Publish(new OrderPlaced(orderId, userId, total));   // 0..n consumers

// MassTransit: consumers are just classes
public class SendConfirmationEmail : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> ctx) { … }
}
public class UpdateAnalytics : IConsumer<OrderPlaced>   // same event
{
    public async Task Consume(ConsumeContext<OrderPlaced> ctx) { … }
}

builder.Services.AddMassTransit(x =>
{
    x.AddConsumers(typeof(Program).Assembly);
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(config["Rabbit:Host"]);
        cfg.ReceiveEndpoint("order-emails", e =>
        {
            e.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1),
                                                 TimeSpan.FromSeconds(30),
                                                 TimeSpan.FromSeconds(2)));
            e.ConfigureConsumer<SendConfirmationEmail>(ctx);
        });
        cfg.ConfigureEndpoints(ctx);
    });
});
```

> **\[Why it matters\]** The choice is really about who owns the decision. A command couples the sender to an outcome; an event lets the system grow reactions without the publisher ever changing. Publishing `SendEmail` as an “event” smuggles the coupling back in through the broker, which is the most common design mistake in messaging.

> **\[Trap\]**
>
> **Common trap.** Both shapes are at-least-once. Consumers must be idempotent — dedupe on `MessageId` with an inbox table — and every endpoint needs a dead-letter queue plus an alert on it. A DLQ nobody watches is data loss with extra steps.

### Integration & Distributed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Integration & Distributed</p>

<p class="pd">Patterns for systems made of parts that can fail independently. These postdate the GoF catalogue entirely, and they are where most modern design difficulty actually lives.</p>

<a id="be-h773"></a>

#### Outbox and Saga

##### The outbox pattern

**Intent:** publish a message and change data atomically, without a distributed transaction. It solves the dual-write problem: two systems, no shared transaction, and either one can fail after the other succeeded.

``` text
  THE DUAL-WRITE PROBLEM

    db.SaveChanges();          // succeeds
    bus.Publish(orderPlaced);  // CRASHES
    -> the order exists, nobody was told. Silent inconsistency.

    bus.Publish(orderPlaced);  // succeeds
    db.SaveChanges();          // CRASHES
    -> everyone was told about an order that does not exist.

  THE OUTBOX: make it ONE local transaction

    BEGIN
      INSERT INTO orders  ...
      INSERT INTO outbox (id, type, payload)   <- the message, as DATA
    COMMIT

    a separate RELAY polls the outbox and publishes.

    either both rows exist or neither does. If the relay crashes after
    publishing and before marking, the message is sent twice - which
    is fine, because the consumer is idempotent.
```

``` sql
-- the partial index keeps the outbox query fast forever, because it
-- only indexes rows that are still pending
CREATE INDEX ix_outbox_pending ON outbox (occurred_at) WHERE published_at IS NULL;

-- and the relay claims a batch with SKIP LOCKED, so several relays can run
SELECT * FROM outbox WHERE published_at IS NULL
ORDER BY occurred_at LIMIT 100
FOR UPDATE SKIP LOCKED;
```

##### Saga

**Intent:** coordinate a business process across several services that have no shared transaction, using *compensating actions* rather than rollback.

``` text
  ORCHESTRATION                      CHOREOGRAPHY

    [ Saga ]                         OrderPlaced
     |  |  |                             |
     v  v  v                         PaymentSvc reacts -> PaymentTaken
   Pay Ship Notify                                            |
                                     ShippingSvc reacts -> Shipped
  one component knows the flow
  and issues commands              nobody knows the whole flow

  + the flow is in ONE place       + no central component
  + easy to see where it is stuck  - debugging spans N services
  + easy to add a step             - adding a step means finding every
  - coupled to every participant     subscriber that must change

  past about three steps: ORCHESTRATION. Choreography is elegant on a
  whiteboard and hard to operate at 3am.
```

| Compensation reality                | Detail                                                                     |
|-------------------------------------|----------------------------------------------------------------------------|
| It is **not** a rollback            | The payment happened. A refund is a *new* business event the customer sees |
| Compensations can fail too          | You need a manual queue and a human. Build it before you need it           |
| Order matters                       | Compensate in reverse order of the steps that succeeded                    |
| Some steps cannot compensate        | An email was sent. Design so irreversible steps come **last**              |
| Compensation must be idempotent     | It will be retried, like everything else                                   |
| Every waiting state needs a timeout | Otherwise a crashed service leaves sagas waiting forever, silently         |

###### The relay, and the ordering guarantee it cannot give you

``` sql
-- claim a batch so several relay instances can run safely
SELECT * FROM outbox
WHERE published_at IS NULL
ORDER BY occurred_at
LIMIT 100
FOR UPDATE SKIP LOCKED;
-- SKIP LOCKED: other relays skip rows this one has claimed, rather than
-- blocking. Without it, N relays serialise and you have one relay with
-- extra steps.
```

``` text
  WHAT THE OUTBOX GUARANTEES, AND WHAT IT DOES NOT

  GUARANTEES
    the message exists if and only if the data change committed
    at-least-once delivery, eventually

  DOES NOT GUARANTEE
    ORDER.

      relay instance A claims rows 1-100
      relay instance B claims rows 101-200
      B finishes first -> message 150 is published BEFORE message 50

    even with ONE relay, a publish RETRY reorders relative to later
    messages.

  SO: if a consumer requires order, you must provide it explicitly.

    per-entity ordering    partition by aggregate id (Kafka key,
                           Service Bus session). Messages for ONE order
                           are ordered; across orders they are not -
                           and that is almost always sufficient.

    global ordering        a single-threaded relay AND a single
                           partition. This caps throughput at one
                           consumer, and is rarely worth it.

    version numbers        include an aggregate version in the event;
                           the consumer ignores anything older than
                           what it has already applied. Ordering
                           becomes the consumer's problem to detect,
                           not the broker's to prevent.
```

| Outbox operational concern             | Handling                                                         |
|----------------------------------------|------------------------------------------------------------------|
| The table grows forever                | Delete published rows after N days, on a schedule                |
| The relay falls behind                 | Alert on the age of the oldest unpublished row, not the count    |
| A poison message                       | An attempts counter; move to a failed table past a threshold     |
| The relay is a single point of failure | Several instances with `SKIP LOCKED`, or CDC                     |
| Polling load on the database           | A partial index on unpublished rows; or replace polling with CDC |
| Duplicate publishes                    | Expected — consumers must be idempotent                          |

The scaling endpoint worth knowing: **change data capture removes the relay entirely**. Debezium reads the database's write-ahead log and publishes from it, so there is no polling, no relay to operate, and no lag beyond replication. The cost is another piece of infrastructure and a tighter coupling to the database's replication mechanism — worth it at high volume, overkill below it.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a81"></a>

#### Observer, domain events and the in-process bus

Something happened; several unrelated parts of the system care. Doing it with direct calls couples the producer to every consumer and makes the producer's transaction span all of them.

``` text
  direct calls (coupled)                    domain events (decoupled)

  PlaceOrder handler                        Order.Place()
    -> email.SendConfirmation()               raises OrderPlaced
    -> inventory.Reserve()                  PlaceOrder handler
    -> analytics.Track()                      saves; the SAME transaction writes the outbox
    -> loyalty.AwardPoints()                dispatcher
                                              -> EmailOnOrderPlaced
  adding a 5th concern edits the handler      -> ReserveStockOnOrderPlaced
  a failure in email fails the order          -> TrackOnOrderPlaced

                                            adding a 5th concern adds a file
```

``` csharp
// the aggregate records what happened; it does not know who cares
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> Events => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public IReadOnlyList<IDomainEvent> Dequeue()
    {
        var copy = _events.ToList(); _events.Clear(); return copy;
    }
}

// handlers are discovered, not registered by the producer
public interface IDomainEventHandler<in T> where T : IDomainEvent
{
    Task HandleAsync(T e, CancellationToken ct);
}

public sealed class ReserveStockOnOrderPlaced(IInventory inv) : IDomainEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced e, CancellationToken ct) =>
        inv.ReserveAsync(e.OrderId, e.Lines, ct);
}
```

###### The three dispatch timings, and which to pick

| When dispatched                   | Handlers see      | Failure means                                | Use for                                                                  |
|-----------------------------------|-------------------|----------------------------------------------|--------------------------------------------------------------------------|
| Before `SaveChanges`              | Uncommitted state | The whole transaction rolls back             | Enriching the same aggregate; validation across aggregates               |
| After `SaveChanges`, same process | Committed state   | The write succeeded, the side effect did not | Best-effort work: cache eviction, in-memory projections                  |
| Via the outbox, another process   | Committed state   | Retried until it succeeds                    | Anything that must eventually happen: email, other services, integration |

``` csharp
// the practical arrangement: side effects that MUST happen go through the outbox,
// in the same transaction, and are dispatched by a relay.
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var events = ChangeTracker.Entries<AggregateRoot>()
        .SelectMany(e => e.Entity.Dequeue()).ToList();

    foreach (var e in events)
        Outbox.Add(OutboxMessage.From(e, _time.GetUtcNow()));   // same transaction

    return await base.SaveChangesAsync(ct);
}
```

> **\[Trap\]**
>
> **Common trap.** An in-process event bus that dispatches synchronously after save looks decoupled and is not: the caller still waits for every handler, and one slow handler makes the endpoint slow. Worse, a handler that throws after commit leaves the system in a state the code never anticipated. If a handler must succeed, it goes through the outbox; if it does not matter, it should not be able to fail the request — catch and log inside the dispatcher.
