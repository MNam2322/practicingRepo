using BookingApp.Core.Admin;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>Shape of the "Venue" configuration section (appsettings.json).</summary>
public sealed class VenueOptions
{
    public const string Section = "Venue";

    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public List<ResourceOptions> Resources { get; set; } = [];

    public sealed class ResourceOptions
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ResourceKind Kind { get; set; } = ResourceKind.Other;
        public int Capacity { get; set; } = 1;
        public int SlotMinutes { get; set; } = 60;
        public int MaxSlotsPerBooking { get; set; } = 2;
        /// <summary>"Monday-Friday": "08:00-20:00" style entries. Days not listed are closed.</summary>
        public Dictionary<string, string> Hours { get; set; } = [];
    }
}

/// <summary>
/// The InMemory storage provider's venue: built from configuration at start-up, then kept in memory, where admins can
/// change it (resources, hours, closures, rules) with an in-memory audit log. Lost on restart, like InMemory bookings.
/// Also the source the database is seeded from on first run (<see cref="BuildCatalog"/>).
/// </summary>
internal sealed class ConfigurationResourceRepository : IResourceRepository, IVenueAdminRepository
{
    private readonly Lock _gate = new();
    private readonly List<Closure> _closures = [];
    private readonly List<AuditEntry> _audit = [];
    private readonly IBookingRepository _bookings;
    private readonly Venue _venue;
    private List<Resource> _resources;
    private BookingPolicy _policy;

    public ConfigurationResourceRepository(IOptions<VenueOptions> venue, IOptions<BookingPolicy> policy, IBookingRepository bookings)
    {
        var catalog = BuildCatalog(venue.Value, policy.Value);
        _venue = catalog.Venue;
        _resources = [.. catalog.Resources];
        _policy = catalog.Policy;
        _bookings = bookings;
    }

    public Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(new ResourceCatalog(_venue, [.. _resources], _policy));
    }

    public Task<IReadOnlyList<Closure>> ClosuresAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Closure> found = [.. _closures.Where(c => c.AppliesTo(resourceId) && c.Time.Overlaps(window)).OrderBy(c => c.Time.Start)];
            return Task.FromResult(found);
        }
    }

    public Task<IReadOnlyList<Closure>> AllClosuresAsync(TimeRange window, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Closure> found = [.. _closures.Where(c => c.Time.Overlaps(window)).OrderBy(c => c.Time.Start)];
            return Task.FromResult(found);
        }
    }

    public Task AddClosureAsync(Closure closure, CancellationToken cancellationToken)
    {
        lock (_gate) _closures.Add(closure);
        return Task.CompletedTask;
    }

    // ---- admin: each change and its audit entry under one lock, so they happen together ----

    public Task<bool> AddResourceAsync(Resource resource, int sortOrder, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        if (_resources.Any(r => string.Equals(r.Id, resource.Id, StringComparison.OrdinalIgnoreCase))) return false;
        _resources.Insert(Math.Min(sortOrder, _resources.Count), resource);
        return true;
    });

    public Task<bool> UpdateResourceAsync(Resource resource, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        var i = _resources.FindIndex(r => string.Equals(r.Id, resource.Id, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        _resources[i] = resource with { Hours = _resources[i].Hours };
        return true;
    });

    public Task<bool> SetHoursAsync(string resourceId, IReadOnlyList<OpeningHours> hours, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        var i = _resources.FindIndex(r => string.Equals(r.Id, resourceId, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        _resources[i] = _resources[i] with { Hours = [.. hours] };
        return true;
    });

    public async Task<bool> HasBookingsAsync(string resourceId, CancellationToken cancellationToken) =>
        (await _bookings.InWindowAsync(TimeRange.Create(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), cancellationToken))
            .Any(b => string.Equals(b.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

    public Task<bool> DeleteResourceAsync(string resourceId, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        var removed = _resources.RemoveAll(r => string.Equals(r.Id, resourceId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) _closures.RemoveAll(c => string.Equals(c.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
        return removed;
    });

    public Task AddClosureAsync(Closure closure, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        _closures.Add(closure);
        return true;
    });

    public Task<bool> DeleteClosureAsync(Guid closureId, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () => _closures.RemoveAll(c => c.Id == closureId) > 0);

    public Task SavePolicyAsync(BookingPolicy policy, AuditEntry audit, CancellationToken cancellationToken) => Change(audit, () =>
    {
        _policy = policy;
        return true;
    });

    public Task<IReadOnlyList<AuditEntry>> AuditAsync(int take, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AuditEntry> found = [.. _audit.Where(a => before is null || a.OccurredAt < before).OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(take)];
            return Task.FromResult(found);
        }
    }

    private Task<bool> Change(AuditEntry audit, Func<bool> change)
    {
        lock (_gate)
        {
            if (!change()) return Task.FromResult(false);
            _audit.Add(audit);
            return Task.FromResult(true);
        }
    }

    public static ResourceCatalog BuildCatalog(VenueOptions o, BookingPolicy policy) =>
        new(new Venue(o.Name, o.TimeZoneId), [.. o.Resources.Select(ToResource)], policy);

    private static Resource ToResource(VenueOptions.ResourceOptions r)
    {
        var hours = new List<OpeningHours>();
        foreach (var (days, span) in r.Hours)
        {
            var times = span.Split('-', StringSplitOptions.TrimEntries);
            var open = TimeOnly.Parse(times[0], System.Globalization.CultureInfo.InvariantCulture);
            var close = TimeOnly.Parse(times[1], System.Globalization.CultureInfo.InvariantCulture);
            if (close <= open) throw new InvalidOperationException($"Resource '{r.Id}': closing time must be after opening time in '{span}'.");
            if ((close - open).TotalMinutes % r.SlotMinutes != 0)
                throw new InvalidOperationException($"Resource '{r.Id}': opening hours '{span}' are not a whole number of {r.SlotMinutes}-minute slots.");
            foreach (var day in ExpandDays(days)) hours.Add(new OpeningHours(day, open, close));
        }
        return new Resource(r.Id, r.Name, r.Kind, r.Capacity, r.SlotMinutes, r.MaxSlotsPerBooking, hours);
    }

    /// <summary>"Monday-Friday", "Saturday", "Sunday" and similar.</summary>
    private static IEnumerable<DayOfWeek> ExpandDays(string days)
    {
        var parts = days.Split('-', StringSplitOptions.TrimEntries);
        var first = Enum.Parse<DayOfWeek>(parts[0], ignoreCase: true);
        var last = parts.Length > 1 ? Enum.Parse<DayOfWeek>(parts[1], ignoreCase: true) : first;
        for (var d = first; ; d = (DayOfWeek)(((int)d + 1) % 7))
        {
            yield return d;
            if (d == last) break;
        }
    }
}
