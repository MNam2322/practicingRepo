using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Admin;

/// <summary>A resource as an admin enters it. The id is a URL slug and cannot change after creation.</summary>
public sealed record ResourceDraft(string Id, string Name, ResourceKind Kind, int Capacity, int SlotMinutes, int MaxSlotsPerBooking);

/// <summary>
/// Admin use cases: resources, opening hours, closures and the booking rules. Each change is validated here, then saved
/// together with an audit entry naming the admin (library 10.2 "Recording what happened"). Callers must hold the
/// ManageVenue policy (checked by the controller).
/// </summary>
public sealed partial class AdminService(IResourceRepository resources, IBookingRepository bookings, IVenueAdminRepository admin, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex Slug();

    public async Task<Result<Resource>> CreateResourceAsync(ResourceDraft draft, Actor actor, CancellationToken cancellationToken)
    {
        if (!Slug().IsMatch(draft.Id))
            return new Error(ErrorKind.RuleViolation, "invalid_resource_id", "Use 2 to 63 lowercase letters, digits and dashes for the id, such as court-3.");
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        if (catalog.Find(draft.Id) is not null) return Exists(draft.Id);
        var resource = new Resource(draft.Id, draft.Name.Trim(), draft.Kind, draft.Capacity, draft.SlotMinutes, draft.MaxSlotsPerBooking, []);
        var audit = Audit(actor, "resource.created", "resource", resource.Id, null, DescribeResource(resource));
        return await admin.AddResourceAsync(resource, catalog.Resources.Count, audit, cancellationToken)
            ? Result<Resource>.Ok(resource)
            : Exists(draft.Id);
    }

    public async Task<Result<Resource>> UpdateResourceAsync(string id, ResourceDraft draft, Actor actor, CancellationToken cancellationToken)
    {
        var current = (await resources.GetCatalogAsync(cancellationToken)).Find(id);
        if (current is null) return NotFound(id);
        var updated = current with
        {
            Name = draft.Name.Trim(),
            Kind = draft.Kind,
            Capacity = draft.Capacity,
            SlotMinutes = draft.SlotMinutes,
            MaxSlotsPerBooking = draft.MaxSlotsPerBooking,
        };
        if (CheckHours(updated.Hours, updated.SlotMinutes) is { } broken) return broken;
        var audit = Audit(actor, "resource.updated", "resource", id, DescribeResource(current), DescribeResource(updated));
        return await admin.UpdateResourceAsync(updated, audit, cancellationToken) ? Result<Resource>.Ok(updated) : NotFound(id);
    }

    public async Task<Result<Resource>> SetHoursAsync(string id, IReadOnlyList<OpeningHours> hours, Actor actor, CancellationToken cancellationToken)
    {
        var current = (await resources.GetCatalogAsync(cancellationToken)).Find(id);
        if (current is null) return NotFound(id);
        if (CheckHours(hours, current.SlotMinutes) is { } broken) return broken;
        var updated = current with { Hours = [.. hours.OrderBy(h => h.Day)] };
        var audit = Audit(actor, "resource.hours_set", "resource", id, DescribeHours(current.Hours), DescribeHours(updated.Hours));
        return await admin.SetHoursAsync(id, updated.Hours, audit, cancellationToken) ? Result<Resource>.Ok(updated) : NotFound(id);
    }

    public async Task<Result<string>> DeleteResourceAsync(string id, Actor actor, CancellationToken cancellationToken)
    {
        var current = (await resources.GetCatalogAsync(cancellationToken)).Find(id);
        if (current is null) return NotFound(id);
        if (await admin.HasBookingsAsync(current.Id, cancellationToken))
            return new Error(ErrorKind.Conflict, "resource_in_use", $"{current.Name} has bookings, so it cannot be deleted. Close it with a closure instead.");
        var audit = Audit(actor, "resource.deleted", "resource", current.Id, DescribeResource(current), null);
        return await admin.DeleteResourceAsync(current.Id, audit, cancellationToken) ? Result<string>.Ok(current.Id) : NotFound(id);
    }

    /// <summary>
    /// Closes one resource, or the whole venue when <paramref name="resourceId"/> is null, for whole venue-local days
    /// (<paramref name="from"/> to <paramref name="to"/>, inclusive), such as a public holiday. Refused while confirmed
    /// bookings fall inside: cancel those first, with a reason.
    /// </summary>
    public async Task<Result<Closure>> AddClosureAsync(string? resourceId, DateOnly from, DateOnly to, string reason, Actor actor, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        Resource? resource = null;
        if (resourceId is not null && (resource = catalog.Find(resourceId)) is null) return NotFound(resourceId);
        if (to < from) return new Error(ErrorKind.RuleViolation, "end_before_start", "The last day must not be before the first day.");
        if (string.IsNullOrWhiteSpace(reason)) return new Error(ErrorKind.RuleViolation, "reason_required", "Say why it is closed.");

        var window = TimeRange.Create(AvailabilityCalculator.DayWindow(catalog.Venue, from).Start, AvailabilityCalculator.DayWindow(catalog.Venue, to).End);
        var affected = resource is null ? catalog.Resources : [resource];
        var inTheWay = 0;
        foreach (var r in affected) inTheWay += (await bookings.BlockingAsync(r.Id, window, cancellationToken)).Count;
        if (inTheWay > 0)
            return new Error(ErrorKind.Conflict, "bookings_in_the_way",
                $"{inTheWay} confirmed booking(s) fall inside those days. Cancel them first, with a reason, then add the closure.");

        var closure = new Closure(Guid.CreateVersion7(), resource?.Id, window, reason.Trim());
        await admin.AddClosureAsync(closure, Audit(actor, "closure.added", "closure", closure.Id.ToString(), null, DescribeClosure(closure)), cancellationToken);
        return Result<Closure>.Ok(closure);
    }

    public async Task<Result<Guid>> DeleteClosureAsync(Guid closureId, Actor actor, CancellationToken cancellationToken)
    {
        var audit = Audit(actor, "closure.deleted", "closure", closureId.ToString(), null, null);
        return await admin.DeleteClosureAsync(closureId, audit, cancellationToken)
            ? Result<Guid>.Ok(closureId)
            : new Error(ErrorKind.NotFound, "closure_not_found", "There is no such closure.");
    }

    public async Task<Result<BookingPolicy>> UpdatePolicyAsync(BookingPolicy policy, Actor actor, CancellationToken cancellationToken)
    {
        var current = (await resources.GetCatalogAsync(cancellationToken)).Policy;
        await admin.SavePolicyAsync(policy, Audit(actor, "policy.updated", "policy", "booking", DescribePolicy(current), DescribePolicy(policy)), cancellationToken);
        return Result<BookingPolicy>.Ok(policy);
    }

    /// <summary>Every closure, resource or venue-wide, that overlaps the window.</summary>
    public Task<IReadOnlyList<Closure>> ClosuresAsync(TimeRange window, CancellationToken cancellationToken) => resources.AllClosuresAsync(window, cancellationToken);

    public Task<IReadOnlyList<AuditEntry>> AuditAsync(int take, DateTimeOffset? before, CancellationToken cancellationToken) =>
        admin.AuditAsync(Math.Clamp(take, 1, 200), before, cancellationToken);

    /// <summary>One entry per day at most, close after open, and a whole number of slots each day.</summary>
    private static Error? CheckHours(IReadOnlyList<OpeningHours> hours, int slotMinutes)
    {
        if (hours.GroupBy(h => h.Day).Any(g => g.Count() > 1))
            return new Error(ErrorKind.RuleViolation, "invalid_hours", "Give each day at most once.");
        foreach (var h in hours)
        {
            if (h.Close <= h.Open)
                return new Error(ErrorKind.RuleViolation, "invalid_hours", $"On {h.Day}, closing time must be after opening time.");
            if ((h.Close - h.Open).TotalMinutes % slotMinutes != 0)
                return new Error(ErrorKind.RuleViolation, "invalid_hours", $"On {h.Day}, {h.Open:HH\\:mm}–{h.Close:HH\\:mm} is not a whole number of {slotMinutes}-minute slots.");
        }
        return null;
    }

    private AuditEntry Audit(Actor actor, string action, string entity, string entityId, string? before, string? after) =>
        new(Guid.CreateVersion7(), time.GetUtcNow(), actor, action, entity, entityId, before, after);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static string DescribeResource(Resource r) =>
        ToJson(new { r.Id, r.Name, r.Kind, r.Capacity, r.SlotMinutes, r.MaxSlotsPerBooking });

    private static string DescribeHours(IEnumerable<OpeningHours> hours) =>
        ToJson(hours.Select(h => new { h.Day, Open = h.Open.ToString("HH:mm"), Close = h.Close.ToString("HH:mm") }));

    private static string DescribeClosure(Closure c) => ToJson(new { c.ResourceId, Start = c.Time.Start, End = c.Time.End, c.Reason });

    private static string DescribePolicy(BookingPolicy p) =>
        ToJson(new { p.MaxDaysAhead, p.MinLeadMinutes, p.CancellationCutoffHours, p.MaxActiveBookingsPerCustomer });

    private static Error NotFound(string id) => new(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{id}'.");

    private static Error Exists(string id) => new(ErrorKind.Conflict, "resource_exists", $"There is already a resource '{id}'.");
}
