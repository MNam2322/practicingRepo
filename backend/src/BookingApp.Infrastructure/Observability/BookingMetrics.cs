using System.Diagnostics.Metrics;
using BookingApp.Core.Common;

namespace BookingApp.Infrastructure.Observability;

/// <summary>
/// The business counters, on a Meter from <see cref="IMeterFactory"/> (so each app, and each test's app, has its own).
/// OpenTelemetry exports the "BookingApp" meter (Api/Telemetry.cs). Tagged by resource, never by customer: a tag value
/// per person would explode the number of series (library 11.6 "Example 2: A metrics bill that tripled").
/// </summary>
public sealed class BookingMetrics : IBookingMetrics
{
    private readonly Counter<long> _held;
    private readonly Counter<long> _conflicts;
    private readonly Counter<long> _confirmed;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _expired;

    public BookingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.Name);
        _held = meter.CreateCounter<long>("bookingapp.bookings.created", "{booking}", "Bookings created (held, before confirmation).");
        _conflicts = meter.CreateCounter<long>("bookingapp.bookings.conflicts", "{booking}", "Booking attempts refused because the time was taken (slot_taken).");
        _confirmed = meter.CreateCounter<long>("bookingapp.bookings.confirmed", "{booking}", "Holds confirmed.");
        _cancelled = meter.CreateCounter<long>("bookingapp.bookings.cancelled", "{booking}", "Bookings and holds cancelled, by the customer or staff.");
        _expired = meter.CreateCounter<long>("bookingapp.holds.expired", "{hold}", "Holds that ran out before being confirmed.");
    }

    public void Held(string resourceId) => _held.Add(1, Resource(resourceId));
    public void Conflict(string resourceId) => _conflicts.Add(1, Resource(resourceId));
    public void Confirmed(string resourceId) => _confirmed.Add(1, Resource(resourceId));
    public void Cancelled(string resourceId, string by) => _cancelled.Add(1, Resource(resourceId), new KeyValuePair<string, object?>("cancelled.by", by));
    public void HoldsExpired(int count) => _expired.Add(count);

    private static KeyValuePair<string, object?> Resource(string resourceId) => new("booking.resource", resourceId);
}
