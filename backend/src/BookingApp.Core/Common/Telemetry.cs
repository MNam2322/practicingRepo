using System.Diagnostics;

namespace BookingApp.Core.Common;

/// <summary>
/// The app's own spans: one per use case (book, confirm, cancel), inside the HTTP request's span and around the database
/// calls, so a booking reads as one trace from request to SQL. Activities are the BCL's tracing API; OpenTelemetry
/// exports them (Api/Telemetry.cs). Library: 11.6 "OpenTelemetry: traces, metrics and logs from one pipeline".
/// </summary>
public static class Telemetry
{
    public const string Name = "BookingApp";

    public static readonly ActivitySource Source = new(Name);

    /// <summary>Records a use case's outcome on its span: "ok", or the error code (slot_taken …).</summary>
    public static void Outcome<T>(Activity? activity, Result<T> result)
    {
        if (activity is null) return;
        activity.SetTag("booking.outcome", result.Error?.Code ?? "ok");
        if (result.Value is Bookings.Booking b) activity.SetTag("booking.id", b.Id);
    }
}

/// <summary>
/// What the business counts: bookings held, conflicts (slot_taken), confirmations, cancellations and expired holds.
/// A port, so Core stays free of the metrics plumbing; Infrastructure counts with a Meter. Metrics, not traces, are for
/// counting: traces are sampled (library 11.6 "Common trap").
/// </summary>
public interface IBookingMetrics
{
    void Held(string resourceId);
    void Conflict(string resourceId);
    void Confirmed(string resourceId);
    void Cancelled(string resourceId, string by);
    void HoldsExpired(int count);
}

/// <summary>Counts nothing: for tests that do not look at metrics.</summary>
public sealed class NoBookingMetrics : IBookingMetrics
{
    public static readonly NoBookingMetrics Instance = new();

    public void Held(string resourceId) { }
    public void Conflict(string resourceId) { }
    public void Confirmed(string resourceId) { }
    public void Cancelled(string resourceId, string by) { }
    public void HoldsExpired(int count) { }
}
