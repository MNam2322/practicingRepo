using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Admin;

/// <summary>
/// Changes to the venue set-up. Every write takes the audit entry that describes it and saves both in one transaction:
/// a change is never recorded without happening, and never happens without being recorded.
/// </summary>
public interface IVenueAdminRepository
{
    /// <summary>False when a resource with that id already exists.</summary>
    Task<bool> AddResourceAsync(Resource resource, int sortOrder, AuditEntry audit, CancellationToken cancellationToken);

    /// <summary>Updates name, kind, capacity, slot length and longest booking. False when it does not exist.</summary>
    Task<bool> UpdateResourceAsync(Resource resource, AuditEntry audit, CancellationToken cancellationToken);

    /// <summary>Replaces the weekly opening hours. False when the resource does not exist.</summary>
    Task<bool> SetHoursAsync(string resourceId, IReadOnlyList<OpeningHours> hours, AuditEntry audit, CancellationToken cancellationToken);

    /// <summary>Whether any booking, in any status, uses the resource (bookings keep their history, so it then stays).</summary>
    Task<bool> HasBookingsAsync(string resourceId, CancellationToken cancellationToken);

    /// <summary>Deletes a resource with no bookings (its hours and closures go with it). False when it does not exist.</summary>
    Task<bool> DeleteResourceAsync(string resourceId, AuditEntry audit, CancellationToken cancellationToken);

    Task AddClosureAsync(Closure closure, AuditEntry audit, CancellationToken cancellationToken);

    /// <summary>False when no such closure exists.</summary>
    Task<bool> DeleteClosureAsync(Guid closureId, AuditEntry audit, CancellationToken cancellationToken);

    Task SavePolicyAsync(BookingPolicy policy, AuditEntry audit, CancellationToken cancellationToken);

    /// <summary>The newest entries first; <paramref name="before"/> pages back in time.</summary>
    Task<IReadOnlyList<AuditEntry>> AuditAsync(int take, DateTimeOffset? before, CancellationToken cancellationToken);
}
