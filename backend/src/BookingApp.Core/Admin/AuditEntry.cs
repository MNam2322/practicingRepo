namespace BookingApp.Core.Admin;

/// <summary>Who did something: the signed-in account, never the database connection (library 10.6 "Record the actor").</summary>
/// <param name="RequestId">The request's trace id, to find the matching server logs.</param>
public sealed record Actor(Guid AccountId, string Email, string? RequestId);

/// <summary>
/// One row of the audit log: who changed what, when, and what it was before and after (JSON). Written in the same
/// transaction as the change, and append-only in the database. Library: 10.2 "Recording what happened",
/// 10.6 "Auditing and compliance".
/// </summary>
/// <param name="Action">Such as "resource.updated" or "policy.updated".</param>
/// <param name="Entity">Such as "resource", "closure", "policy".</param>
public sealed record AuditEntry(
    Guid Id, DateTimeOffset OccurredAt, Actor Actor, string Action, string Entity, string EntityId, string? Before, string? After);
