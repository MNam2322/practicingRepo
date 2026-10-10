using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// The booking reads that run most, in one place: EfBookingRepository runs them, and tools/BookingApp.Perf measures
/// exactly these (their plans and timings are in docs/performance.md).
/// <para>
/// The overlap test <c>start &lt; windowEnd AND windowStart &lt; end</c> has two open ranges, and a B-tree seeks one: on
/// its own it reads every booking that starts before the window's end (library 7.9 Case 1, rows read vs returned). No
/// booking is longer than <see cref="Booking.MaxLength"/>, so one that overlaps the window starts after
/// windowStart − MaxLength: that third, redundant predicate turns the search into a bounded range on start_utc.
/// </para>
/// </summary>
internal static class BookingQueries
{
    /// <summary>The earliest start a booking overlapping a window can have.</summary>
    public static DateTimeOffset EarliestOverlappingStart(TimeRange window) => window.Start - Booking.MaxLength;

    /// <summary>Confirmed bookings and holds for one resource that overlap a window, by start (availability).</summary>
    public static IQueryable<BookingRow> Blocking(BookingDbContext db, string resourceId, TimeRange window)
    {
        var start = window.Start;
        var end = window.End;
        var from = EarliestOverlappingStart(window);
        return db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == resourceId && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending)
                && b.StartUtc > from && b.StartUtc < end && start < b.EndUtc)
            .OrderBy(b => b.StartUtc);
    }

    /// <summary>Every booking (any status, every resource) overlapping a window, by start (the staff day schedule).</summary>
    public static IQueryable<BookingRow> InWindow(BookingDbContext db, TimeRange window)
    {
        var start = window.Start;
        var end = window.End;
        var from = EarliestOverlappingStart(window);
        return db.Bookings.AsNoTracking()
            .Where(b => b.StartUtc > from && b.StartUtc < end && start < b.EndUtc)
            .OrderBy(b => b.StartUtc);
    }

    /// <summary>One customer's bookings, by start (My bookings).</summary>
    public static IQueryable<BookingRow> ForCustomer(BookingDbContext db, Guid customerId) =>
        db.Bookings.AsNoTracking()
            .Where(b => b.CustomerId == customerId)
            .OrderBy(b => b.StartUtc);
}
